using UnityEngine;
using TMPro;

public class MatchTimer : MonoBehaviour
{
    public TextMeshProUGUI timerText;

    private float timeRemaining = 180f;

    void Update()
    {
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
        }
    }
}