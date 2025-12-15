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
using System;
using Random = UnityEngine.Random;
using Unity.Mathematics;
using Unity.Properties;
using System.Linq.Expressions;
using System.Transactions;

public class SPHSolver : MonoBehaviour
{
    public float gravitiy;
    public float smoothingRadius = 2.0f;
    private float particleMass = 1.0f;
    public float targetDensity;
    public float pressureMultiplier;

    private LineRenderer lr;
    public Vector2 BoundsSize; 
    public float CollisionDamping; 

    private List<FluidParticle> particles = new List<FluidParticle>(); // Dynamic list 
    public int numParticles; 
    public GameObject particlePrefab;
    public float particleRadius = 0.2f;
    public float particleSpacing;

    public static SPHSolver Instance;

    void Awake()
    {
        // -- Created the GameObject for visualizing the collision box --
        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.widthMultiplier = 0.05f;
        lr.positionCount = 4;
        lr.useWorldSpace = true;
        Material defaulLineMaterial= new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        lr.material = defaulLineMaterial;
        lr.startColor = Color.white;
        lr.endColor = Color.white;

        Instance = this; 
    }

    void Start()
    {
        SpawnInitialParticles();

    }

    void Update()
    {

        CalculateDensities();

        foreach (var p in particles)
        {

            Vector2 pressureForce = CalculatePressureForce(p);
            Vector2 pressureAcceleration = pressureForce / p.density;

            Vector2 gravity = new Vector2(0, -gravitiy);  // Apply gravity 
            p.velocity += (gravity + pressureAcceleration) * Time.deltaTime;

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

        int particlesPerRow = (int)Math.Sqrt(numParticles);
        float spacing = particleRadius * 2 + particleSpacing; 

        for (int i = 0; i < numParticles; i++)
        {
            int row = i / particlesPerRow;
            int col = i % particlesPerRow;

            float x = (col - particlesPerRow / 2f) * spacing;
            float y = (row - particlesPerRow / 2f ) * spacing;
            // float x = Random.Range(-15,15);
            // float y = Random.Range(-12,12);

            SpawnParticle(new Vector2(x,y)); 
            Debug.Log(i + "position:" + "x:" + x + "y" + y);
        }
        
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
            Debug.Log($"X Collision! pos.x={pos.x}, halfBounds={halfBoundsSize.x}");
            pos.x = Math.Clamp(pos.x, -halfBoundsSize.x, halfBoundsSize.x);
            vel.x *= -1 * CollisionDamping;
        }
        if (Mathf.Abs(pos.y) > halfBoundsSize.y)
        {
            pos.y = Math.Clamp(pos.y, -halfBoundsSize.y, halfBoundsSize.y);
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

    void CalculateDensities()
    {
        foreach (var pA in particles)
        {
            float density = 0f;

            foreach(var pB in particles)
            {
                float distance = Vector2.Distance(pA.position, pB.position);
                float influence = SmoothingKernel(smoothingRadius, distance);
                density += particleMass * influence;
            }
            
            pA.density = density;
        }
    }

    float SmoothingKernel(float radius, float distance)
    {
        if (distance >= radius) return 0f;
    
        float volume =  (Mathf.PI * Mathf.Pow(radius, 4)) / 6f;
        return (radius - distance) * (radius - distance) / volume;
    }

    float SmoothingKernelDerivative(float radius, float distance)
    {
        if (distance >= radius) return 0f;

        float scale = 12 / (Mathf.Pow(radius, 4) * Mathf.PI);
        return (distance - radius) * scale;
    }

    float CalculateProperty(Vector2 samplePoint, System.Func<FluidParticle, float> getter)
    {
        float property = 0;

        foreach(var p in particles)
        {
            float distance = (p.position - samplePoint).magnitude;
            float influence = SmoothingKernel(smoothingRadius, distance);
            float density = p.density;
            if (density > 0)
            {
                property += getter(p) * influence * particleMass / density;
            }
        }

        return property;
    }

    Vector2 CalculatePressureForce(FluidParticle p)
    {
        Vector2 pressureForce = Vector2.zero;

        foreach (var otherParticle in particles)
        {
            if (p == otherParticle) continue;  // Skip self
        
            float distance = Vector2.Distance(p.position, otherParticle.position);

            if (distance > 0 && otherParticle.density > 0)
            {
                Vector2 direction = (otherParticle.position - p.position) / distance;
                float slope = SmoothingKernelDerivative(smoothingRadius, distance);
                
                // Use SHARED pressure between the two particles
                float sharedPressure = (ConvertDensityToPressure(p.density) + ConvertDensityToPressure(otherParticle.density)) / 2f;
                
                pressureForce += sharedPressure * direction * slope * particleMass / otherParticle.density;
            }
            else if(distance == 0)
            {
                Vector2 dirrection = UnityEngine.Random.insideUnitCircle.normalized;
                pressureForce += dirrection * 0.0001f;
            }
        }
        return pressureForce;
    }

    float ConvertDensityToPressure(float density)
    {
        float densityErr = density - targetDensity;
        float pressure = densityErr * pressureMultiplier;
        return pressure;
    }

    void OnDrawGizmos()
    {
        if (particles == null || particles.Count == 0) return;
        
        foreach(var p in particles)
        {
            // Color based on density
            float densityRatio = p.density / targetDensity;
            
            if (densityRatio < 1.0f)
            {
                Gizmos.color = Color.Lerp(Color.blue, Color.green, densityRatio);
            }
            else
            {
                Gizmos.color = Color.Lerp(Color.green, Color.red, Mathf.Min(densityRatio - 1.0f, 1.0f));
            }
            
            Gizmos.DrawWireSphere(p.position, particleRadius * 2);
            // Gizmos.color = Color.cyan;
            // Gizmos.DrawWireSphere(p.position, smoothingRadius);
        }
    }
}