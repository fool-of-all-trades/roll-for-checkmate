using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    // Singleton
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        // klasyczny wzorzec singletonu — zostajemy, jeœli nikt inny jeszcze nie istnieje
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // by GameManager przetrwa³ prze³adowanie scen
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // pomijamy TitleScreen na razie
        LoadBoard();
    }

    /// <summary>
    /// Prze³¹cza na scenê z plansz¹ szachów
    /// Dodaæ j¹ trzeba do Build Settings pod t¹ nazw¹
    /// </summary>
    public void LoadBoard()
    {
        SceneManager.LoadScene("BoardScene");
    }

    /// <summary>
    /// Koñczy grê – wraca do sceny tytu³owej i resetuje stan w GameControllerze
    /// </summary>
    public void EndGame()
    {
        SceneManager.LoadScene("TitleScene");

        //GameController.Instance.ResetGame();
    }
}
