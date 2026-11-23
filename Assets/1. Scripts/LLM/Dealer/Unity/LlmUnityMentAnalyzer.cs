using System;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

namespace Poker.Llm.Unity
{
    // 1단계(interpreterAgent)가 짧은 연상 메모를 쓰고, 2단계(agent)가 멘트와 메모를 JSON으로 변환한다.
    // 1단계가 없거나 실패하면 멘트만으로 2단계를 실행한다.
    // 이 오브젝트는 씬에서 비활성으로 두고, Host 딜러가 시작될 때만 켜서 Client는 모델을 로딩하지 않는다.
    [DisallowMultipleComponent]
    public class LlmUnityMentAnalyzer : MonoBehaviour, IMentAnalyzer
    {
        private const int MaxInterpretationLength = 120;

        [Tooltip("JSON 변환 에이전트")]
        [SerializeField] private LLMAgent agent;
        [Tooltip("연상 메모 에이전트. 비워두면 해석 단계를 건너뛴다. agent와 다른 컴포넌트여야 한다.")]
        [SerializeField] private LLMAgent interpreterAgent;
        [SerializeField, Min(0.1f)] private float timeoutSeconds = 5f;
        [SerializeField, Min(0.1f)] private float interpreterTimeoutSeconds = 4f;
        [SerializeField, Min(1)] private int interpreterMaxTokens = 80;
        [SerializeField] private bool warmupOnStart = true;

        // Both agents use the same native service. A timed-out response still owns it until Chat returns.
        private readonly SemaphoreSlim nativeGate = new SemaphoreSlim(1, 1);
        private Task drain;
        private bool retryQuit;

        private CancellationTokenSource lifetime;
        private Task warmup;

        public bool IsReady => lifetime != null && warmup?.IsCompleted == true && IsAgentReady(agent);

        private bool UsesInterpreter => interpreterAgent != null && interpreterAgent != agent;

        private void Awake()
        {
            Application.wantsToQuit += PrepareToQuit;
            if (agent == null)
            {
                Debug.LogError($"[{nameof(LlmUnityMentAnalyzer)}] LLMAgent is not assigned.", this);
                return;
            }

            agent.slot = 0;
            agent.systemPrompt = MentAnalysisPrompt.SystemPrompt;
            agent.grammar = MentAnalysisPrompt.JsonSchema;
            agent.temperature = 0f;
            agent.numPredict = 64;
            agent.save = "";

            if (interpreterAgent == agent)
            {
                Debug.LogWarning($"[{nameof(LlmUnityMentAnalyzer)}] interpreterAgent must be a different LLMAgent. Interpretation is disabled.", this);
            }
            if (!UsesInterpreter) return;

            interpreterAgent.slot = interpreterAgent.llm == agent.llm ? 1 : 0;
            interpreterAgent.systemPrompt = MentAnalysisPrompt.InterpreterSystemPrompt;
            interpreterAgent.grammar = "";
            interpreterAgent.temperature = 0f;
            interpreterAgent.numPredict = interpreterMaxTokens;
            interpreterAgent.save = "";
        }

        private void OnEnable()
        {
            if (drain != null) return;
            lifetime = new CancellationTokenSource();
            warmup = warmupOnStart && agent != null ? WarmupAsync(lifetime.Token) : Task.CompletedTask;
        }

        private void OnDisable()
        {
            StopAcceptingRequests();
#if UNITY_EDITOR_WIN
            RetainLoadedLlamaLibraries();
#endif

        }

        private void OnDestroy() => Application.wantsToQuit -= PrepareToQuit;

        private void StopAcceptingRequests()
        {
            var stop = lifetime;
            lifetime = null;
            if (stop == null) return;
            stop.Cancel();
            stop.Dispose();
        }

        public void Retire()
        {
            // Room departure can also destroy the session's hierarchy.
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
            DestroyAfterDrain();
        }

        private async void DestroyAfterDrain()
        {
            await DrainAsync();
            if (this != null) Destroy(gameObject);
        }

        private Task DrainAsync()
        {
            StopAcceptingRequests();
            return drain ??= WaitForNativeResponse();
        }

        private async Task WaitForNativeResponse()
        {
            await nativeGate.WaitAsync();
            nativeGate.Release();
        }

        private bool PrepareToQuit()
        {
            if (DrainAsync().IsCompleted) return true;
            if (!retryQuit)
            {
                retryQuit = true;
                QuitAfterDrain();
            }
            return false;
        }

        private async void QuitAfterDrain()
        {
            await drain;
            Application.Quit();
        }

        private async Task WarmupAsync(CancellationToken token)
        {
            try
            {
                while (!IsAgentReady(agent))
                {
                    if (agent == null || agent.llm == null || agent.llm.failed) return;
                    await Task.Delay(50, token);
                }

                // Both agents share the native model. Prepare their first contexts in order.
                await ChatAsync(agent, nativeGate, null, timeoutSeconds, token, warmup: true);
                if (UsesInterpreter && IsAgentReady(interpreterAgent))
                    await ChatAsync(interpreterAgent, nativeGate, null,
                        interpreterTimeoutSeconds, token, warmup: true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(LlmUnityMentAnalyzer)}] Warmup skipped: {ex.Message}", this);
            }
        }

        public async Task<MentAnalysisResult> AnalyzeAsync(string ment, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ment)) return MentAnalysisResult.Success(MentAnalysis.NoIntent, null);
            if (agent == null) return MentAnalysisResult.Unavailable("LLMAgent is not assigned.");
            if (lifetime == null) return MentAnalysisResult.Cancelled();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            cancellationToken = request.Token;

            string interpretation;
            try
            {
                interpretation = await InterpretAsync(ment, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return MentAnalysisResult.Cancelled();
            }

            MentAnalysisResult result = await ConvertAsync(ment, interpretation, cancellationToken);
            return result.WithInterpretation(interpretation);
        }

        // 해석은 보조 단계이므로 호출자 취소 외의 실패는 null로 넘기고 2단계를 계속한다.
        private async Task<string> InterpretAsync(string ment, CancellationToken cancellationToken)
        {
            if (!UsesInterpreter || !IsAgentReady(interpreterAgent)) return null;

            try
            {
                string reply = await ChatAsync(interpreterAgent, nativeGate,
                    MentAnalysisPrompt.BuildInterpreterMessage(ment), interpreterTimeoutSeconds, cancellationToken);
                return CleanInterpretation(reply);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{nameof(LlmUnityMentAnalyzer)}] Interpretation skipped: {ex.Message}", this);
                return null;
            }
        }

        private async Task<MentAnalysisResult> ConvertAsync(string ment, string interpretation, CancellationToken cancellationToken)
        {
            if (!IsReady) return MentAnalysisResult.Unavailable("LLM is not ready.");

            string raw;
            try
            {
                raw = await ChatAsync(agent, nativeGate,
                    MentAnalysisPrompt.BuildUserMessage(ment, interpretation), timeoutSeconds, cancellationToken);
            }
            catch (TimeoutException)
            {
                return MentAnalysisResult.Timeout();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return MentAnalysisResult.Cancelled();
            }
            catch (Exception ex)
            {
                return MentAnalysisResult.Unavailable(ex.Message);
            }

            return MentAnalysisParser.TryParse(raw, out MentAnalysis analysis, out string error)
                ? MentAnalysisResult.Success(analysis, raw)
                : MentAnalysisResult.InvalidResponse(error, raw);
        }

        // Start the timeout after acquiring the agent, and keep the slot reserved until native work exits.
        private static async Task<string> ChatAsync(LLMAgent target, SemaphoreSlim gate, string message,
            float timeoutSeconds, CancellationToken cancellationToken, bool warmup = false)
        {
            await gate.WaitAsync(cancellationToken);
            Task<string> chat = null;
            int originalNumPredict = target.numPredict;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                if (warmup) target.numPredict = 0;
                chat = target.Chat(message, null, null, false);
                Task deadline = Task.Delay(Timeout.Infinite, timeout.Token);

                if (await Task.WhenAny(chat, deadline) == chat)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await chat;
                }

                // Return the deadline to gameplay. Cancelling the native slot while it closes its
                // response reader can abort the process; its bounded generation drains in finally.
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"{target.name} timed out.");
            }
            finally
            {
                if (warmup) target.numPredict = originalNumPredict;
                if (chat == null || chat.IsCompleted)
                {
                    if (chat != null && chat.IsFaulted) _ = chat.Exception;
                    gate.Release();
                }
                else
                {
                    _ = ReleaseWhenFinishedAsync(chat, gate);
                }
            }
        }

        private static async Task ReleaseWhenFinishedAsync(Task chat, SemaphoreSlim gate)
        {
            try { await chat.ConfigureAwait(false); }
            catch (Exception) { /* The caller has already received the timeout or cancellation. */ }
            finally { gate.Release(); }
        }

        private static string CleanInterpretation(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;

            int thinkEnd = reply.LastIndexOf("</think>", StringComparison.Ordinal);
            if (thinkEnd >= 0) reply = reply.Substring(thinkEnd + "</think>".Length);

            foreach (string line in reply.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return trimmed.Length > MaxInterpretationLength ? trimmed.Substring(0, MaxInterpretationLength) : trimmed;
            }
            return null;
        }

        private static bool IsAgentReady(LLMAgent target) =>
            target != null && target.llm != null && target.llm.started && !target.llm.failed;

#if UNITY_EDITOR_WIN
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureWindowsEditorBackend()
        {
            // Keep native logging from entering a managed domain during teardown.
            if (LLMUnitySetup.DebugMode < LLMUnitySetup.DebugModeType.Warning)
                LLMUnitySetup.DebugMode = LLMUnitySetup.DebugModeType.Warning;

            // tinyBLAS was observed running code after its DLL was unloaded on Play/Stop.
            var exclusions = UndreamAI.LlamaLib.LlamaLib.libraryExclusion;
            exclusions.Remove("cublas");
            if (!exclusions.Contains("tinyblas")) exclusions.Add("tinyblas");

            UnityEditor.EditorApplication.playModeStateChanged -= RetainNativeLibrariesOnExit;
            UnityEditor.EditorApplication.playModeStateChanged += RetainNativeLibrariesOnExit;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= RetainLoadedLlamaLibraries;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RetainLoadedLlamaLibraries;
        }

        private static void RetainNativeLibrariesOnExit(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) RetainLoadedLlamaLibraries();
        }

        private static void RetainLoadedLlamaLibraries()
        {
            // The model service is disposed as usual. Keep native code mapped until the Editor exits.
            const uint pin = 1; // GET_MODULE_HANDLE_EX_FLAG_PIN
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            foreach (System.Diagnostics.ProcessModule module in process.Modules)
            {
                if (module.ModuleName.StartsWith("llamalib_win-", StringComparison.OrdinalIgnoreCase))
                    GetModuleHandleEx(pin, module.FileName, out _);
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetModuleHandleExW",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetModuleHandleEx(uint flags, string fileName, out IntPtr module);
#endif
    }
}
