using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using TMPro;
using System;
using Photon.Realtime;

public class ConnectionManager : MonoBehaviourPunCallbacks
{
    [SerializeField] private Button connectionButton;
    [SerializeField] private TextMeshProUGUI connectionText;

    private void Awake()
    {
        connectionButton.interactable = false;
    }

    public void Start()
    {
        PhotonNetwork.NickName = "Player" + UnityEngine.Random.Range(1000, 10000);
        Debug.Log("Player nick: " + PhotonNetwork.NickName);
        PhotonNetwork.GameVersion = "1";
        PhotonNetwork.ConnectUsingSettings();
    }

    // public void CreateRoom()
    // {
    //     PhotonNetwork.CreateRoom(null, new Photon.Realtime.RoomOptions { MaxPlayers = 2 });
    // }

    // public void JoinRoom()
    // {
    //     PhotonNetwork.JoinRandomRoom();
    // }

    public override void OnConnected()
    {
        connectionButton.interactable = true;
    }

    public override void OnJoinedRoom()
    {
        connectionText.text = "Waiting players";
        LockStepManager.Instance.PrepGameStart();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("OnConnectedToMaster");
    }

    public void Connect()
    {
        PhotonNetwork.JoinOrCreateRoom("Room", new RoomOptions() { MaxPlayers = 2, PublishUserId = true }, TypedLobby.Default);
        connectionButton.interactable = false;
        connectionText.text = "Connecting";
    }

    private IEnumerator Wait(System.Action callback = null)
    {
        var waiter = new WaitForSeconds(.1f);

        while (PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount < 2)
        {
            Debug.Log(PhotonNetwork.CurrentRoom?.PlayerCount);
            yield return waiter;
        }

        connectionText.text = "CONNECT";
        callback?.Invoke();
    }
}
