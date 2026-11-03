using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI scoreText;

    private int p1Score = 0;
    private int p2Score = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        UpdateScoreDisplay();
    }

    public void AddPoint(int playerIndex)
    {
        if (playerIndex == 1) p1Score++;
        else if (playerIndex == 2) p2Score++;

        UpdateScoreDisplay();
    }

    private void UpdateScoreDisplay()
    {
        if (scoreText == null) return;

        // Rich text: Blue for P1, Red for P2, gray for the divider
        scoreText.text = $"<color=#ef4444>{p1Score}</color>  <color=#64748b>:</color>  <color=#38bdf8>{p2Score}</color>";
    }

    public void ResetScores()
    {
        p1Score = 0;
        p2Score = 0;
        UpdateScoreDisplay();
    }
}