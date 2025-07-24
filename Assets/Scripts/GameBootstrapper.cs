//using UnityEngine;
//using Board; 

//public class GameBootstrapper : MonoBehaviour
//{
//    [Header("View")]
//    [SerializeField] private ChessBoardView boardView;

//    [Header("Prefabs & parents")]
//    [SerializeField] private Transform piecesParent;
//    [SerializeField] private GameObject pawnPr, rookPr, knightPr;
//    [SerializeField] private GameObject bishopPr, queenPr, kingPr;

//    [Header("Input Handler")]
//    [SerializeField] private BoardInputHandler inputHandler;

//    private IChessBoardModel _model;
//    private IChessBoardService _boardService;

//    void Awake()
//    {
//        // 1) Create model
//        _model = new ChessBoardModel();

//        // 2) Create service, inject model + view + controller stub + prefabs + parent
//        _boardService = new ChessBoardService(
//            _model,
//            boardView,
//            pawnPr, rookPr, knightPr,
//            bishopPr, queenPr, kingPr,
//            piecesParent
//        );

//        boardView.Initialise(_model);

//        // 3) Initialize view + input
//        // boardView.Initialize(/* might want to add an Initialize(IChessBoardService) overload */);
//        inputHandler.Initialize(_boardService);

//        // 4) Kick off the board
//        _boardService.InitializeBoard();
//    }
//}
