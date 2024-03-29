﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Diagnostics;
using System.Text;
using System;

[System.Serializable]
public abstract class Action
{
    public int NetworkAverage { get; set; }
    public int RuntimeAverage { get; set; }

    public virtual void ProcessAction() { }
}

[System.Serializable]
public class NoAction : Action
{
    public override void ProcessAction()
    {

    }
}

public class LockStepManager : MonoBehaviour
{
    [SerializeField] private PhotonView photonView;
    [SerializeField] private GameObject mainMenuPanel;

    #region Public Variables
    public static readonly int FirstLockStepTurnID = 0;

    public static LockStepManager Instance;

    public int LockStepTurnID = FirstLockStepTurnID;

    public int numberOfPlayers;
    #endregion

    #region Private Variables
    private PendingActions pendingActions;
    private ConfirmedActions confirmedActions;

    private Queue<Action> actionsToSend;

    private List<string> readyPlayers;
    private List<string> playersConfirmedImReady;

    private bool initialized = false; //indicates if we are initialized and ready for game start

    //Variables for adjusting Lockstep and GameFrame length
    RollingAverage networkAverage;
    RollingAverage runtimeAverage;
    long currentGameFrameRuntime; //used to find the maximum gameframe runtime in the current lockstep turn
    private Stopwatch gameTurnSW;
    private int initialLockStepTurnLength = 200; //in Milliseconds
    private int initialGameFrameTurnLength = 50; //in Milliseconds
    private int LockstepTurnLength;
    private int GameFrameTurnLength;
    private int GameFramesPerLockstepTurn;
    private int LockstepsPerSecond;
    private int GameFramesPerSecond;

    private int playerIDToProcessFirst = 0; //used to rotate what player's action gets processed first

    private int GameFrame = 0; //Current Game Frame number in the currect lockstep turn
    private int AccumilatedTime = 0; //the accumilated time in Milliseconds that have passed since the last time GameFrame was called
    #endregion

    // Use this for initialization
    void Awake()
    {
        enabled = false;
        Instance = this;
    }

    #region GameStart
    public void InitGameStartLists()
    {
        if (initialized) { return; }

        readyPlayers = new List<string>(numberOfPlayers);
        playersConfirmedImReady = new List<string>(numberOfPlayers);

        initialized = true;
    }

    public void PrepGameStart()
    {
        LockStepTurnID = FirstLockStepTurnID;
        numberOfPlayers = 2;
        pendingActions = new PendingActions(this);
        confirmedActions = new ConfirmedActions(this);
        actionsToSend = new Queue<Action>();

        gameTurnSW = new Stopwatch();
        currentGameFrameRuntime = 0;
        networkAverage = new RollingAverage(numberOfPlayers, initialLockStepTurnLength);
        runtimeAverage = new RollingAverage(numberOfPlayers, initialGameFrameTurnLength);

        InitGameStartLists();

        photonView.RPC("ReadyToStart", RpcTarget.AllBuffered, PhotonNetwork.LocalPlayer.UserId);
    }

    private void CheckGameStart()
    {
        if (playersConfirmedImReady == null)
        {
            return;
        }
        //check if all expected players confirmed our gamestart message
        if (playersConfirmedImReady.Count == numberOfPlayers)
        {
            //check if all expected players sent their gamestart message
            if (readyPlayers.Count == numberOfPlayers)
            {
                //we are ready to start

                //we no longer need these lists
                playersConfirmedImReady = null;
                readyPlayers = null;

                GameStart();
            }
        }
    }

    private void GameStart()
    {
        //start the LockStep Turn loop
        photonView.RPC("GameOn", RpcTarget.AllBuffered);
    }

    [PunRPC]
    public void ReadyToStart(string playerID)
    {
        //make sure initialization has already happened -incase another player sends game start before we are ready to handle it
        InitGameStartLists(); ;

        readyPlayers.Add(playerID);

        if (PhotonNetwork.IsMasterClient)
        {
            //don't need an rpc call if we are the server
            ConfirmReadyToStartServer(PhotonNetwork.LocalPlayer.UserId /*confirmingPlayerID*/, playerID /*confirmedPlayerID*/);
        }
        else
        {
            photonView.RPC("ConfirmReadyToStartServer", RpcTarget.MasterClient, PhotonNetwork.LocalPlayer.UserId /*confirmingPlayerID*/, playerID /*confirmedPlayerID*/);
        }

        //Check if we can start the game
        CheckGameStart();
    }

    [PunRPC]
    public void ConfirmReadyToStartServer(string confirmingPlayerID, string confirmedPlayerID)
    {
        if (!PhotonNetwork.IsMasterClient) { return; } //workaround when multiple players running on same machine

        //validate ID
        if (GetPlayerById(confirmedPlayerID) == null)
        {
            //TODO: error handling
            return;
        }

        //relay message to confirmed client
        if (PhotonNetwork.LocalPlayer.UserId.Equals(confirmedPlayerID))
        {
            //don't need an rpc call if we are the server
            ConfirmReadyToStart(confirmedPlayerID, confirmingPlayerID);
        }
        else
        {
            photonView.RPC("ConfirmReadyToStart", RpcTarget.OthersBuffered, confirmedPlayerID, confirmingPlayerID);
        }

    }

    [PunRPC]
    public void ConfirmReadyToStart(string confirmedPlayerID, string confirmingPlayerID)
    {
        if (!PhotonNetwork.LocalPlayer.UserId.Equals(confirmedPlayerID)) { return; }

        //log.Debug ("Player " + confirmingPlayerID + " confirmed I am ready to start the game.");
        playersConfirmedImReady.Add(confirmingPlayerID);

        //Check if we can start the game
        CheckGameStart();
    }
    #endregion

    #region Actions
    public void AddAction(Action action)
    {
        if (!initialized)
        {
            return;
        }
        actionsToSend.Enqueue(action);
    }

    private bool LockStepTurn()
    {
        //Check if we can proceed with the next turn
        bool nextTurn = NextTurn();
        if (nextTurn)
        {
            SendPendingAction();
            //the first and second lockstep turn will not be ready to process yet
            if (LockStepTurnID >= FirstLockStepTurnID + 3)
            {
                ProcessActions();
            }
        }
        //otherwise wait another turn to recieve all input from all players

        UpdateGameFrameRate();
        return nextTurn;
    }

    /// <summary>
    /// Check if the conditions are met to proceed to the next turn.
    /// If they are it will make the appropriate updates. Otherwise 
    /// it will return false.
    /// </summary>
    private bool NextTurn()
    {
        //		log.Debug ("Next Turn Check: Current Turn - " + LockStepTurnID);
        //		log.Debug ("    priorConfirmedCount - " + confirmedActions.playersConfirmedPriorAction.Count);
        //		log.Debug ("    currentConfirmedCount - " + confirmedActions.playersConfirmedCurrentAction.Count);
        //		log.Debug ("    allPlayerCurrentActionsCount - " + pendingActions.CurrentActions.Count);
        //		log.Debug ("    allPlayerNextActionsCount - " + pendingActions.NextActions.Count);
        //		log.Debug ("    allPlayerNextNextActionsCount - " + pendingActions.NextNextActions.Count);
        //		log.Debug ("    allPlayerNextNextNextActionsCount - " + pendingActions.NextNextNextActions.Count);

        if (confirmedActions.ReadyForNextTurn())
        {
            if (pendingActions.ReadyForNextTurn())
            {
                //increment the turn ID
                LockStepTurnID++;
                //move the confirmed actions to next turn
                confirmedActions.NextTurn();
                //move the pending actions to this turn
                pendingActions.NextTurn();

                return true;
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("Have not recieved player(s) actions: ");
                foreach (int i in pendingActions.WhosNotReady())
                {
                    sb.Append(i + ", ");
                }
            }
        }
        else
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Have not recieved confirmation from player(s): ");
            foreach (int i in pendingActions.WhosNotReady())
            {
                sb.Append(i + ", ");
            }
        }

        //		if(confirmedActions.ReadyForNextTurn() && pendingActions.ReadyForNextTurn()) {
        //			//increment the turn ID
        //			LockStepTurnID++;
        //			//move the confirmed actions to next turn
        //			confirmedActions.NextTurn();
        //			//move the pending actions to this turn
        //			pendingActions.NextTurn();
        //			
        //			return true;
        //		}

        return false;
    }

    private void SendPendingAction()
    {
        Action action = null;
        if (actionsToSend.Count > 0)
        {
            action = actionsToSend.Dequeue();
        }

        //if no action for this turn, send the NoAction action
        if (action == null)
        {
            action = new NoAction();
        }

        //action.NetworkAverage = Network.GetLastPing (Network.connections[0/*host player*/]);
        if (LockStepTurnID > FirstLockStepTurnID + 1)
        {
            action.NetworkAverage = confirmedActions.GetPriorTime();
        }
        else
        {
            action.NetworkAverage = initialLockStepTurnLength;
        }
        action.RuntimeAverage = Convert.ToInt32(currentGameFrameRuntime);
        //clear the current runtime average
        currentGameFrameRuntime = 0;

        //add action to our own list of actions to process
        pendingActions.AddAction(action, GetPlayerIndexById(PhotonNetwork.LocalPlayer.UserId), LockStepTurnID, LockStepTurnID);
        //start the confirmed action timer for network average
        confirmedActions.StartTimer();
        //confirm our own action
        confirmedActions.ConfirmAction(GetPlayerIndexById(PhotonNetwork.LocalPlayer.UserId), LockStepTurnID, LockStepTurnID);
        //send action to all other players
        photonView.RPC("RecieveAction", RpcTarget.Others, LockStepTurnID, PhotonNetwork.LocalPlayer.UserId, BinarySerialization.SerializeObjectToByteArray(action));
    }

    private void ProcessActions()
    {
        //process action should be considered in runtime performance
        gameTurnSW.Start();

        //Rotate the order the player actions are processed so there is no advantage given to
        //any one player
        for (int i = playerIDToProcessFirst; i < pendingActions.CurrentActions.Length; i++)
        {
            pendingActions.CurrentActions[i].ProcessAction();
            runtimeAverage.Add(pendingActions.CurrentActions[i].RuntimeAverage, i);
            networkAverage.Add(pendingActions.CurrentActions[i].NetworkAverage, i);
        }

        for (int i = 0; i < playerIDToProcessFirst; i++)
        {
            pendingActions.CurrentActions[i].ProcessAction();
            runtimeAverage.Add(pendingActions.CurrentActions[i].RuntimeAverage, i);
            networkAverage.Add(pendingActions.CurrentActions[i].NetworkAverage, i);
        }

        playerIDToProcessFirst++;
        if (playerIDToProcessFirst >= pendingActions.CurrentActions.Length)
        {
            playerIDToProcessFirst = 0;
        }

        //finished processing actions for this turn, stop the stopwatch
        gameTurnSW.Stop();
    }

    [PunRPC]
    public void RecieveAction(int lockStepTurn, string playerID, byte[] actionAsBytes)
    {
        //log.Debug ("Recieved Player " + playerID + "'s action for turn " + lockStepTurn + " on turn " + LockStepTurnID);
        Action action = BinarySerialization.DeserializeObject<Action>(actionAsBytes);
        if (action == null)
        {
            //TODO: Error handle invalid actions recieve
        }
        else
        {
            pendingActions.AddAction(action, GetPlayerIndexById(playerID), LockStepTurnID, lockStepTurn);

            //send confirmation
            if (PhotonNetwork.IsMasterClient)
            {
                //we don't need an rpc call if we are the server
                ConfirmActionServer(lockStepTurn, PhotonNetwork.LocalPlayer.UserId, playerID);
            }
            else
            {
                photonView.RPC("ConfirmActionServer", RpcTarget.MasterClient, lockStepTurn, PhotonNetwork.LocalPlayer.UserId, playerID);
            }
        }
    }

    [PunRPC]
    public void ConfirmActionServer(int lockStepTurn, string confirmingPlayerID, string confirmedPlayerID)
    {
        if (!PhotonNetwork.IsMasterClient) { return; } //Workaround - if server and client on same machine

        //log.Debug("ConfirmActionServer called turn:" + lockStepTurn + " playerID:" + confirmingPlayerID);
        //log.Debug("Sending Confirmation to player " + confirmedPlayerID);

        if (PhotonNetwork.LocalPlayer.UserId.Equals(confirmedPlayerID))
        {
            //we don't need an RPC call if this is the server
            ConfirmAction(lockStepTurn, confirmingPlayerID);
        }
        else
        {
            photonView.RPC("ConfirmAction", GetPlayerById(confirmedPlayerID), lockStepTurn, confirmingPlayerID);
        }
    }

    [PunRPC]
    public void ConfirmAction(int lockStepTurn, string confirmingPlayerID)
    {
        confirmedActions.ConfirmAction(GetPlayerIndexById(confirmingPlayerID), LockStepTurnID, lockStepTurn);
    }
    #endregion

    #region Game Frame
    private void UpdateGameFrameRate()
    {
        //log.Debug ("Runtime Average is " + runtimeAverage.GetMax ());
        //log.Debug ("Network Average is " + networkAverage.GetMax ());
        LockstepTurnLength = (networkAverage.GetMax() * 2/*two round trips*/) + 1/*minimum of 1 ms*/;
        GameFrameTurnLength = runtimeAverage.GetMax();

        //lockstep turn has to be at least as long as one game frame
        if (GameFrameTurnLength > LockstepTurnLength)
        {
            LockstepTurnLength = GameFrameTurnLength;
        }

        GameFramesPerLockstepTurn = LockstepTurnLength / GameFrameTurnLength;
        //if gameframe turn length does not evenly divide the lockstep turn, there is extra time left after the last
        //game frame. Add one to the game frame turn length so it will consume it and recalculate the Lockstep turn length
        if (LockstepTurnLength % GameFrameTurnLength > 0)
        {
            GameFrameTurnLength++;
            LockstepTurnLength = GameFramesPerLockstepTurn * GameFrameTurnLength;
        }

        LockstepsPerSecond = (1000 / LockstepTurnLength);
        if (LockstepsPerSecond == 0) { LockstepsPerSecond = 1; } //minimum per second

        GameFramesPerSecond = LockstepsPerSecond * GameFramesPerLockstepTurn;
    }

    //called once per unity frame
    public void Update()
    {
        //Basically same logic as FixedUpdate, but we can scale it by adjusting FrameLength
        AccumilatedTime = AccumilatedTime + Convert.ToInt32((Time.deltaTime * 1000)); //convert sec to milliseconds

        //in case the FPS is too slow, we may need to update the game multiple times a frame
        while (AccumilatedTime > GameFrameTurnLength)
        {
            GameFrameTurn();
            AccumilatedTime = AccumilatedTime - GameFrameTurnLength;
        }
    }

    private void GameFrameTurn()
    {
        //first frame is used to process actions
        if (GameFrame == 0)
        {
            if (!LockStepTurn())
            {
                //if the lockstep turn is not ready to advance, do not run the game turn
                return;
            }
        }

        //start the stop watch to determine game frame runtime performance
        gameTurnSW.Start();

        //update game
        
        List<IHasGameFrame> finished = new List<IHasGameFrame>();
        foreach (IHasGameFrame obj in SceneManager.Manager.GameFrameObjects)
        {
            obj.GameFrameTurn(GameFramesPerSecond);
            if (obj.Finished)
            {
                finished.Add(obj);
            }
        }

        foreach (IHasGameFrame obj in finished)
        {
            SceneManager.Manager.GameFrameObjects.Remove(obj);
        }

        GameFrame++;
        if (GameFrame == GameFramesPerLockstepTurn)
        {
            GameFrame = 0;
        }

        //stop the stop watch, the gameframe turn is over
        gameTurnSW.Stop();
        //update only if it's larger - we will use the game frame that took the longest in this lockstep turn
        long runtime = Convert.ToInt32((Time.deltaTime * 1000))/*deltaTime is in secounds, convert to milliseconds*/ + gameTurnSW.ElapsedMilliseconds;
        if (runtime > currentGameFrameRuntime)
        {
            currentGameFrameRuntime = runtime;
        }
        //clear for the next frame
        gameTurnSW.Reset();
    }
    #endregion

    private Player GetPlayerById(string id)
    {
        for(int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            if(PhotonNetwork.PlayerList[i].UserId == id)
            {
                return PhotonNetwork.PlayerList[i];
            }
        }

        return null;
    }

    private int GetPlayerIndexById(string id)
    {
        for(int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            if(PhotonNetwork.PlayerList[i].UserId == id)
            {
                return i;
            }
        }

        return -1;
    }

    [PunRPC]
    public void GameOn()
    {
        enabled = true;
        PlanetsController.Instance.SpawnPlanets();
        mainMenuPanel.SetActive(false);
    }
}
