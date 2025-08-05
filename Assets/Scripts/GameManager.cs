using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    // Singleton
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // so that the GameManager persists across scene loads
        DontDestroyOnLoad(gameObject);
    }

    //public enum PlayerType
    //{
    //    White, Black, None
    //}

    //private PlayerType _currentPlayer = PlayerType.White;

    //public override void OnNetworkSpawn()
    //{
    //    //base.OnNetworkSpawn();
    //    if(NetworkManager.Singleton.LocalClientId == 0)
    //    {
    //        _currentPlayer = PlayerType.White;
    //    }
    //    else
    //    {
    //        _currentPlayer = PlayerType.Black;
    //    }
    //}

    private void Start()
    {
        // skipping TitleScene for now
        LoadBoard();
    }

    public void LoadBoard()
    {
        SceneManager.LoadScene("BoardScene");
    }

    public void EndGame()
    {
        SceneManager.LoadScene("TitleScene");

        //GameController.Instance.ResetGame();
    }
}
