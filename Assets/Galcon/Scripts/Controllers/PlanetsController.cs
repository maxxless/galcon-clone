using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;

public class PlanetsController : MonoBehaviour
{
    public static PlanetsController Instance { get; private set; }

    [SerializeField] private LayerMask planetMask;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private Vector2Int planetsCount;
    [SerializeField] private PhotonView photonView;

    [Header("WIN")]
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TextMeshProUGUI winText;

    [Header("OTHER")]
    [Range(0f, 1f)] [SerializeField] private float substractPercentage;
    [SerializeField] private TextMeshProUGUI substractPercentageText;

    private List<Planet> allPlanets = new List<Planet>();
    private Planet lastSelectedPlanet = null;

    public float SubstractPercentage => substractPercentage;

    public bool IsSpawned { get; private set; } = false;

    private void Awake()
    {
        Instance = this;
        winPanel.SetActive(false);
    }

    private void Start()
    {
        substractPercentageText.text = ((int)(substractPercentage * 100f)).ToString();
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            var selectedPlanet = CheckPlanetInput();
            selectedPlanet?.ShowCountText();

            if (lastSelectedPlanet != null && selectedPlanet != lastSelectedPlanet)
            {
                lastSelectedPlanet.HideCountText();
                lastSelectedPlanet = selectedPlanet;
            }
            else if (selectedPlanet != null)
            {
                lastSelectedPlanet = selectedPlanet;
            }

        }
        else if (Input.GetMouseButtonDown(1))
        {
            var selectedPlanet = CheckPlanetInput();

            if (selectedPlanet != null && lastSelectedPlanet != null && lastSelectedPlanet != selectedPlanet && !lastSelectedPlanet.IsEnemyPlanet)
                lastSelectedPlanet.SubstractPlanesRPC(selectedPlanet);
        }

        if (Input.GetAxis("Mouse ScrollWheel") > 0f) // forward
        {
            substractPercentage += 0.01f;

            if (substractPercentage > 1f)
            {
                substractPercentage = 1f;
            }

            substractPercentageText.text = ((int)(substractPercentage * 100f)).ToString();
        }
        else if (Input.GetAxis("Mouse ScrollWheel") < 0f) // backwards
        {
            substractPercentage -= 0.01f;

            if (substractPercentage < 0.01f)
            {
                substractPercentage = 0.011f;
            }

            substractPercentageText.text = ((int)(substractPercentage * 100f)).ToString();
        }
    }

    private Planet CheckPlanetInput()
    {
        RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero, 200f, planetMask);

        if (hit.collider != null && hit.collider.gameObject.CompareTag("Planet"))
        {
            var selectedPlanet = hit.transform.GetComponent<Planet>();
            return selectedPlanet;
        }
        else
        {
            return null;
        }
    }

    public void InitPlanet(Planet planet)
    {
        allPlanets.Add(planet);
    }

    public void SpawnPlanets()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        int rndPlanetsCount = Random.Range(planetsCount.x, planetsCount.y + 1);
        bool isMyPlanets = true;
        bool isBigPlanet = true;


        for (int i = 0; i < rndPlanetsCount * 2; i++)
        {
            var spawnedPlanet = PhotonNetwork.Instantiate("MyPlanet", spawnPoints[i].position, Quaternion.identity).GetComponent<Planet>();

            if (i == rndPlanetsCount)
            {
                isBigPlanet = true;
            }

            if (i > rndPlanetsCount - 1)
            {
                isMyPlanets = false;
            }

            if (isBigPlanet)
            {
                spawnedPlanet.InitRPC(isMyPlanets, 1.25f, 64, .5f);
                isBigPlanet = false;
            }
            else
            {
                spawnedPlanet.InitRPC(isMyPlanets, 1f, 32, 1f);
            }
        }

        ReleaseBlocksRPC();
    }

    public void ReleaseBlocksRPC()
    {
        photonView.RPC("ReleaseBlocks", RpcTarget.AllBuffered);
    }

    [PunRPC]
    public void ReleaseBlocks()
    {
        IsSpawned = true;
    }

    public void CheckPlanets()
    {
        for (int i = 0; i < allPlanets.Count; i++)
        {
            if (allPlanets[i].IsEnemyPlanet)
            {
                return;
            }
        }

        SendWinRPC(PhotonNetwork.NickName);
    }

    public void SendWinRPC(string playerNick)
    {
        photonView.RPC("SendWin", RpcTarget.AllBuffered, playerNick);
    }

    [PunRPC]
    public void SendWin(string playerNick)
    {
        Time.timeScale = 0f;
        winPanel.SetActive(true);
        winText.text = playerNick + " wins!";
    }
}
