using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using TMPro;
using UnityEngine;


public class Planet : MonoBehaviour
{
    [SerializeField] private bool isEnemyPlanet;
    [SerializeField] private bool isBigPlanet;
    [SerializeField] private int planesCount;
    [SerializeField] private float planeInSeconds;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color homeColor;
    [SerializeField] private Color enemyColor;
    [SerializeField] private TextMeshPro countText;
    [SerializeField] private PhotonView photonView;
    [SerializeField] private PlaneComponent plane;


    public bool IsEnemyPlanet => isEnemyPlanet;
    public Color HomeColor => homeColor;
    public SpriteRenderer SSpriteRenderer => spriteRenderer;

    private float counter = 0f;
    private bool isInited = false;

    private float accumilatedTime = 0f;
    private float frameLength = 0.05f; //50 miliseconds
    private int FramesCount = 0;


    private void Start()
    {
        PlanetsController.Instance.InitPlanet(this);
        countText.enabled = false;
    }

    private void Update()
    {
        if (!PlanetsController.Instance.IsSpawned)
            return;

        ////// LOCKSTEP
        if (FramesCount == 0)
        {
            CountIntervalForTextUpdate(frameLength);
            FramesCount++;
        }
        else
        {
            accumilatedTime += Time.deltaTime;
            while (accumilatedTime > frameLength)
            {
                CountIntervalForTextUpdate(frameLength);
                accumilatedTime -= frameLength;
                FramesCount++;
            }
        }
        //////
    }

    private void CountIntervalForTextUpdate(float t)
    {
        if (counter >= planeInSeconds)
        {
            planesCount++;
            UpdateCountText();
            counter = 0;
        }
        else
        {
            counter += t;
        }
    }

    public void InitRPC(bool isMine, float scale, int planesCount, float planeInSeconds)
    {
        photonView.RPC("Init", RpcTarget.AllBuffered, isMine, scale, planesCount, planeInSeconds);
    }

    [PunRPC]
    public void Init(bool isMine, float scale, int planesCount, float planeInSeconds)
    {
        if (!PhotonNetwork.IsMasterClient)
            isMine = !isMine;

        if (isMine)
        {
            spriteRenderer.color = homeColor;
        }
        else
        {
            spriteRenderer.color = enemyColor;
        }

        isEnemyPlanet = !isMine;
        transform.localScale = Vector3.one * scale;
        this.planesCount = planesCount;
        this.planeInSeconds = planeInSeconds;
        isInited = true;
    }

    public void LandPlane(Planet from)
    {
        if (isEnemyPlanet == from.IsEnemyPlanet)
        {
            planesCount = Land();
        }
        else
        {
            planesCount = (planesCount - 1) == 0 ? Conquer() : planesCount - 1;
        }

        UpdateCountText();

        int Conquer()
        {
            if (from.isEnemyPlanet)
            {
                isEnemyPlanet = true;
                spriteRenderer.color = enemyColor;
            }
            else
            {
                isEnemyPlanet = false;
                spriteRenderer.color = homeColor;
            }

            PlanetsController.Instance.CheckPlanets();

            return 0;
        }

        int Land()
        {
            return planesCount + 1;
        }
    }


    public void SubstractPlanesRPC(Planet to)
    {
        photonView.RPC("SubstractPlanes", RpcTarget.AllBuffered, to.transform.position.x, to.transform.position.y);
    }

    [PunRPC]
    public void SubstractPlanes(float x, float y)
    {
        int planesToSubstract = (int)(planesCount * PlanetsController.Instance.SubstractPercentage);

        if (planesToSubstract > 0)
        {
            planesCount -= planesToSubstract;
            UpdateCountText();

            for (int i = 0; i < planesToSubstract; i++)
            {
                PlaneComponent newPlane = Instantiate(plane.gameObject, transform.position, Quaternion.identity).GetComponent<PlaneComponent>();
                newPlane.Init(this, new Vector3(x, y, 0f));
            }
        }
    }

    private void UpdateCountText()
    {
        countText.text = planesCount.ToString();
    }

    public void ShowCountText()
    {
        UpdateCountText();
        countText.enabled = true;
    }

    public void HideCountText()
    {
        countText.enabled = false;
    }
}
