using UnityEngine;
using TMPro;

/// <summary>
/// Round timer with public API for the combat system.
/// BoxingEnvironment controls start/stop/reset; the timer updates the UI text.
/// </summary>
public class MatchTimer : MonoBehaviour
{
    public TextMeshProUGUI timerText;

    [SerializeField] private float roundDuration = 180f;

    private float timeRemaining;
    private bool isRunning = true;

    /// <summary>Seconds remaining in the round.</summary>
    public float TimeRemaining => timeRemaining;

    /// <summary>Total round duration in seconds.</summary>
    public float RoundDuration => roundDuration;

    /// <summary>Time remaining normalized to 0–1 (1 = full time, 0 = expired).</summary>
    public float NormalizedTimeRemaining => Mathf.Clamp01(timeRemaining / roundDuration);

    /// <summary>Whether the timer is currently counting down.</summary>
    public bool IsRunning => isRunning;

    private void Awake()
    {
        timeRemaining = roundDuration;
    }

    void Update()
    {
        if (!isRunning) return;

        if (timeRemaining > 0)
        {
            timeRemaining -= Time.deltaTime;

            int minutes = Mathf.FloorToInt(timeRemaining / 60);
            int seconds = Mathf.FloorToInt(timeRemaining % 60);

            timerText.text = string.Format("{0}:{1:00}", minutes, seconds);
        }
        else
        {
            timeRemaining = 0;
            timerText.text = "0:00";
            isRunning = false;
        }
    }

    /// <summary>Start or resume the countdown.</summary>
    public void StartTimer()
    {
        isRunning = true;
    }

    /// <summary>Pause the countdown.</summary>
    public void StopTimer()
    {
        isRunning = false;
    }

    /// <summary>Reset to full duration and start counting.</summary>
    public void ResetTimer()
    {
        timeRemaining = roundDuration;
        isRunning = true;
    }
}