using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlaneComponent : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float speed;

    private Planet home = null;
    private Vector3 target = Vector3.zero;

    private float accumilatedTime = 0f;
    private float frameLength = 0.05f; //50 miliseconds
    private int FramesCount = 0;

    private void Update()
    {
        if (home == null)
            return;

        ////// LOCKSTEP
        if (FramesCount == 0)
        {
            MakeMove(frameLength);
            FramesCount++;
        }
        else
        {
            accumilatedTime += Time.deltaTime;

            while (accumilatedTime > frameLength)
            {
                MakeMove(frameLength);
                accumilatedTime -= frameLength;
                FramesCount++;
            }
        }
        //////
    }

    private void MakeMove(float t)
    {
        transform.LookAt(target);
        transform.position += transform.forward * speed * t;
    }

    public void Init(Planet home, Vector3 to)
    {
        this.home = home;
        target = to;
        spriteRenderer.color = home.SSpriteRenderer.color;
        transform.position = home.transform.position + Random.insideUnitSphere;
        transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.tag == "Planet")
        {
            var planet = other.transform.GetComponent<Planet>();

            if (planet != null && Vector2.Distance(transform.position, target) < 2f && planet != home)
            {
                planet.LandPlane(home);
                Destroy(gameObject);
            }
        }
    }
}
