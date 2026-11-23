using System;
using Poker.Foundation;

namespace Poker.Application
{
    /// <summary>A practice observer sees only public table talk and newly exposed cards.</summary>
    public sealed class HoldemPracticeClues
    {
        private static readonly string[][] SuitWords =
        {
            new[] { "클로버", "클럽", "♣" }, new[] { "다이아", "♦" },
            new[] { "하트", "♥" }, new[] { "스페이드", "♠" }
        };
        private static readonly string[] Requests = { "한 장", "한장", "부탁", "주세요", "줘", "나왔으면" };
        private static readonly string[] Refusals = { "말고", "싫", "그만", "빼", "지겨", "필요 없", "필요없", "안 나", "안나" };
        private readonly int[] mentioned = new int[5];
        private string handId;
        private long readThrough;
        private int boardCount;
        public int Suspect { get; private set; }

        public void Observe(string hand, int[] board, HoldemChatEntryPacket[] publicChat)
        {
            if (string.IsNullOrEmpty(hand) || board == null || publicChat == null) return;
            if (hand != handId || board.Length < boardCount)
            {
                handId = hand;
                boardCount = board.Length;
                readThrough = publicChat.Length == 0 ? -1 : publicChat[publicChat.Length - 1].sequence;
                Array.Clear(mentioned, 0, mentioned.Length);
                Suspect = 0;
                return;
            }
            if (board.Length > boardCount)
            {
                int revealed = 0;
                for (int i = boardCount; i < board.Length; i++)
                    revealed |= 1 << (int)Card.FromId(board[i]).Suit;
                Suspect = 0;
                for (int seat = 1; seat < mentioned.Length; seat++)
                {
                    int hint = mentioned[seat];
                    if (hint == 0 || (hint & (hint - 1)) != 0 || (hint & revealed) == 0) continue;
                    if (Suspect != 0) { Suspect = 0; break; }
                    Suspect = seat;
                }
                Array.Clear(mentioned, 0, mentioned.Length);
                boardCount = board.Length;
            }
            foreach (var line in publicChat)
            {
                if (line.sequence <= readThrough) continue;
                readThrough = line.sequence;
                if (line.seat < 1 || line.seat >= mentioned.Length || string.IsNullOrEmpty(line.text)) continue;
                if (!Array.Exists(Requests, line.text.Contains) || Array.Exists(Refusals, line.text.Contains)) continue;
                for (int i = 0; i < SuitWords.Length; i++)
                    if (Array.Exists(SuitWords[i], line.text.Contains)) mentioned[line.seat] |= 1 << (i + 1);
            }
        }

        public void PassOnClue() => Suspect = 0;
    }
}
