using UnityEngine;

internal sealed class Play2TableAudio : MonoBehaviour
{
    private AudioSource source;
    private AudioClip chip, card, turn, result, check, fold, caught, missed;
    private float lastCard = -1, lastCheck = -1, lastFold = -1;

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0;
        source.volume = .55f;
        chip = Resources.Load<AudioClip>("TableAudio/ChipLand");
        card = Resources.Load<AudioClip>("TableAudio/CardSlide");
        turn = Resources.Load<AudioClip>("TableAudio/YourTurn");
        result = Resources.Load<AudioClip>("TableAudio/HandEnd");
        check = Resources.Load<AudioClip>("TableAudio/CheckTap");
        fold = Resources.Load<AudioClip>("TableAudio/CardFold");
        caught = Resources.Load<AudioClip>("TableAudio/AccusationCaught");
        missed = Resources.Load<AudioClip>("TableAudio/AccusationMissed");
    }

    internal void ChipLand() => Play(chip, .7f);
    internal void YourTurn() => Play(turn, .45f);
    internal void HandEnd() => Play(result, .5f);
    internal void Accusation(bool success) => Play(success ? caught : missed, .5f);

    internal void Check()
    {
        if (Time.unscaledTime - lastCheck < .1f) return;
        lastCheck = Time.unscaledTime;
        Play(check, .4f);
    }

    internal void Fold()
    {
        if (Time.unscaledTime - lastFold < .1f) return;
        lastFold = Time.unscaledTime;
        Play(fold, .5f);
    }

    internal void CardSlide()
    {
        if (Time.unscaledTime - lastCard < .09f) return;
        lastCard = Time.unscaledTime;
        Play(card, .5f);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip != null) source.PlayOneShot(clip, volume);
    }
}
