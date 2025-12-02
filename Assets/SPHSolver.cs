using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using UnityEngine.Rendering;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using Unity.VisualScripting;
using UnityEngine.UIElements;
using UnityEngine.Timeline;

public class SPHSolver : MonoBehaviour
{
    private LineRenderer lr;

    public float gravitiy;

    public Vector2 BoundsSize; 
    public float CollisionDamping; 

    private List<FluidParticle> particles = new List<FluidParticle>(); // Dynamic list 
    public GameObject particlePrefab;
    public float particleRadius = 0.2f;

    void Awake()
    {
        // -- Created the GameObject for visualizing the collision box --
        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.widthMultiplier = 0.05f;
        lr.positionCount = 4;
        lr.useWorldSpace = false;
        lr.startColor = Color.white;
        lr.endColor = Color.white;
    }

    void Start()
    {
        SpawnInitialParticles();
    }

    void Update()
    {
        foreach (var p in particles)
        {

            p.force = new Vector2(0, -gravitiy);  // Apply gravity 

            p.velocity += p.force * Time.deltaTime;

            p.position += p.velocity * Time.deltaTime;

            ResolveCollisions(p);
        }
    }

    void SpawnInitialParticles()
    {
        if (particlePrefab == null)
        {
            Debug.LogError("Particle prefab not assigned");
            return;
        }

        Vector2 InitialPos = new Vector2(0,0);
        SpawnParticle(InitialPos);
        
    }

   // -- Spawn a single particle-- 
    public void SpawnParticle(Vector2 position)
    {
        
        GameObject obj = Instantiate(particlePrefab, new Vector3 (position.x, position.y, 0), Quaternion.identity, transform);
        // Set diameter = radius x 2
        obj.transform.localScale = Vector3.one * particleRadius * 2;

        FluidParticle particle = obj.GetComponent<FluidParticle>();
        if (particle == null)
        {
            particle = obj.AddComponent<FluidParticle>();
        }

        particle.velocity = Vector2.zero;
        particles.Add(particle);
    }

    void ResolveCollisions(FluidParticle p)
    {
        UpdateCollisionBox();
        Vector2 halfBoundsSize = BoundsSize / 2 - Vector2.one * particleRadius;
        Vector2 pos = p.position;
        Vector2 vel = p.velocity;


        // Limits & apply collision damping 
        if (Mathf.Abs(pos.x) > halfBoundsSize.x)
        {
            pos.x = halfBoundsSize.x * Mathf.Sign(pos.x);
            vel.x *= -1 * CollisionDamping;
        }
        if (Mathf.Abs(pos.y) > halfBoundsSize.y)
        {
            pos.y = halfBoundsSize.y * Mathf.Sign(pos.y);
            vel.y *= -1 * CollisionDamping;
        }

        p.position = pos;
        p.velocity = vel;
    }

    void UpdateCollisionBox()
    {
        Vector2 half = BoundsSize / 2f;

        Vector3[] corners =
        {
            new Vector3(-half.x, -half.y, 0),
            new Vector3(-half.x,  half.y, 0),
            new Vector3( half.x,  half.y, 0),
            new Vector3( half.x, -half.y, 0)
        };

        lr.SetPositions(corners);
    }
}
