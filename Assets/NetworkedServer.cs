using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccounts;

    const int playerAccountNameAndPassword = 1;

    string playerAccountDataPath;

    int playerWaitingForMatchWithID = -1;

    int playerJoiningAsObserverID = -1;
    int observerReferenceID = -1;

   // int playerWaitingForRematchID = -1;

    bool player1Rematch = false;
    bool player2Rematch = false;


    LinkedList<GameRoom> gameRooms;
    Queue<PlayerMove> playersMoves;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccountDataPath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";

        playerAccounts = new LinkedList<PlayerAccount>();

        LoadPlayerAccount();

        gameRooms = new LinkedList<GameRoom>();
        playersMoves = new Queue<PlayerMove>();

    }

    // Update is called once per frame
    void Update()
    {
        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }
    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);


        //
        //Create Account  
        //
        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            Debug.Log("Create Account");

            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameInUse = true;
                    break;
                }
            }

            if (nameInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id);
            }
            else
            {
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);

                SavePlayerAccount();
            }

            //If not, create new account, add to list and save to list
            //Send Client success or failure
        }

        //
        //Login Check 
        //
        else if (signifier == ClientToServerSignifiers.Login)
        {
            Debug.Log("Login to Account");

            string n = csv[1];
            string p = csv[2];

            bool hasNameBeenFound = false;
            bool hasMsgBeenSentToClient = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    hasNameBeenFound = true;

                    if (pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", id);
                        hasMsgBeenSentToClient = true;
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                        hasMsgBeenSentToClient = true;
                    }
                }
                else
                {
                    //?
                }
            }

            if (!hasNameBeenFound)
            {
                if (!hasMsgBeenSentToClient)
                {
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                }
            }

            //Check if player account already exists
            //Send client success/failure

        }

        //
        //Join Game ROom Queue Check 
        //
        else if (signifier == ClientToServerSignifiers.JoinQueueForGameRoom)
        {

            //Observer Check
            //GameRoom gameRoomAsObserver = null;

            if (playerJoiningAsObserverID == 0)
            {
                Debug.Log("Observer");
                GameRoom gr = GetGameRoomWithClientID(observerReferenceID);

                gr.AddObserver(id);

                SendMessageToClient(ServerToClientSignifiers.JoinAsObserver + "", id);
            }

            if (playerWaitingForMatchWithID == -1)
            {
                playerWaitingForMatchWithID = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);

                gameRooms.AddLast(gr);

                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerID2);

                playerJoiningAsObserverID = 0;
                observerReferenceID = id;

                playerWaitingForMatchWithID = -1;
            }
        }


        //
        // Play Game 
        //
        else if (signifier == ClientToServerSignifiers.PlayGame)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                SendMessageToClient(ServerToClientSignifiers.SetPlayerNumber + "," + 1, gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.SetPlayerNumber + "," + 2, gr.playerID2);
            }
        }

        //Turn Taken 
        else if (signifier == ClientToServerSignifiers.TurnTaken)
        {
            string nodeMark = csv[1];
            string nodeID = csv[2];

            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.PlayersTurn + "", gr.playerID2);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.PlayersTurn + "", gr.playerID1);
                }

                SendMessageToClient(ServerToClientSignifiers.UpdateGameboard + "," + nodeMark + "," + nodeID, gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.UpdateGameboard + "," + nodeMark + "," + nodeID, gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.UpdateGameboard + "," + nodeMark + "," + nodeID, gr.observerID);


                //Save Move to Player Move Queue for Replay System 
                PlayerMove playerMove = new PlayerMove(id, int.Parse(nodeID));
                playersMoves.Enqueue(playerMove);
            }
        }

        //Player Win 
        else if (signifier == ClientToServerSignifiers.PlayerWin)
        {

            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr.playerID1 == id)
            {
                //Player 1 has Won - 0 = Lose, 1 = Win, 3 = Observer
                SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 1, gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 0, gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 3, gr.observerID);
            }
            else if (gr.playerID2 == id)
            {
                //Player 2 has Won - 0 = Lose, 1 = Win, 3 = Observer
                SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 1, gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 0, gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 3, gr.observerID);
            }
        }

        else if (signifier == ClientToServerSignifiers.PlayerMessage)
        {
            string playerMsg = csv[1];

            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr.playerID1 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.DisplayPlayer1Message + "," + playerMsg, gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.DisplayPlayer1Message + "," + playerMsg, gr.observerID);
                SendMessageToClient(ServerToClientSignifiers.DisplayPlayer2Message + "," + playerMsg, gr.playerID2);
            }
            else if (gr.playerID2 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.DisplayPlayer1Message + "," + playerMsg, gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.DisplayPlayer2Message + "," + playerMsg, gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.DisplayPlayer2Message + "," + playerMsg, gr.observerID);
            }
        }

        //Replay 
        else if (signifier == ClientToServerSignifiers.RequestReplayMove)
        {
            Queue<PlayerMove> movesToDequeue = new Queue<PlayerMove>();
            movesToDequeue = playersMoves;

            PlayerMove moveToSend = movesToDequeue.Dequeue();

            SendMessageToClient(ServerToClientSignifiers.ReplayMove + "," + moveToSend.playerID + "," + moveToSend.nodeID, id);
        }

        //Rematch
        else if (signifier == ClientToServerSignifiers.PlayerRequestRematch)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr.playerID1 == id)
            {
                player1Rematch = true;
            }
            else if (gr.playerID2 == id)
            {
                player2Rematch = true;
            }

            if (player1Rematch && player2Rematch)
            {
                SendMessageToClient(ServerToClientSignifiers.RematchConfirmed + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.RematchConfirmed + "", gr.playerID2);
            }
        }
        
        else if (signifier == ClientToServerSignifiers.TieGame)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 4, gr.playerID1);
            SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 4, gr.playerID2);
            SendMessageToClient(ServerToClientSignifiers.EndGame + "," + 3, gr.observerID);
        }

    }
        //
        // Player Account Functions 
        //

        private void SavePlayerAccount()
        {
            StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt");

            foreach (PlayerAccount pa in playerAccounts)
            {
                sw.WriteLine(playerAccountNameAndPassword + "," + pa.name + "," + pa.password);
            }

            sw.Close();
        }

        private void LoadPlayerAccount()
        {

            if (File.Exists(playerAccountDataPath))
            {
                StreamReader sr = new StreamReader(playerAccountDataPath);

                string line;

                while ((line = sr.ReadLine()) != null)
                {
                    string[] csv = line.Split(',');

                    int signifier = int.Parse(csv[0]);

                    if (signifier == playerAccountNameAndPassword)
                    {
                        PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                        playerAccounts.AddLast(pa);
                    }
                }

                sr.Close();
            }
        }


        //
        //Game Room Functions 
        //
        private GameRoom GetGameRoomWithClientID(int id)
        {
            foreach (GameRoom gr in gameRooms)
            {
                if (gr.playerID1 == id || gr.playerID2 == id)
                {
                    return gr;
                }
            }
            return null;
        }




    
}


    //
    //Player Account Class
    //
    public class PlayerAccount
    {
        public string name;
        public string password;

        public PlayerAccount(string Name, string Password)
        {
            name = Name;
            password = Password;
        }
    }

    public class PlayerMove
    {
        public int playerID;
        public int nodeID;

        public PlayerMove(int pID, int nID)
        {
            playerID = pID;
            nodeID = nID;
        }
    }



    //
    //Game Room Class
    //
    public class GameRoom
    {
        public int playerID1;
        public int playerID2;

        public int observerID;

        public bool CanHaveObserver;

        public GameRoom(int PlayerID1, int PlayerID2)
        {
            playerID1 = PlayerID1;
            playerID2 = PlayerID2;
            CanHaveObserver = true;
        }

        public void AddObserver(int observerId)
        {
            observerID = observerId;
        }
    }


public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;

    public const int Login = 2;

    public const int JoinQueueForGameRoom = 3;

    public const int PlayGame = 4;

    public const int TurnTaken = 5;

    public const int PlayerWin = 6;

    public const int PlayerMessage = 7;

    public const int RequestReplayMove = 8;

    public const int PlayerRequestRematch = 9;

    public const int TieGame = 10;
}

    public static class ServerToClientSignifiers
    {
        public const int LoginComplete = 1;

        public const int LoginFailed = 2;

        public const int AccountCreationComplete = 3;

        public const int AccountCreationFailed = 4;

        public const int GameStart = 5;

        public const int SetPlayerNumber = 6;

        public const int PlayersTurn = 7;

        public const int UpdateGameboard = 8;

        public const int EndGame = 9;

        public const int DisplayPlayer1Message = 10;

        public const int DisplayPlayer2Message = 11;

        public const int JoinAsObserver = 12;

        public const int ReplayMove = 13;

        public const int RematchConfirmed = 14;
    }
