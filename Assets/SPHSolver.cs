using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class SPHSolver : MonoBehaviour
{
    private List<FluidParticle> particles = new List<FluidParticle>(); // Dynamic list 
    public GameObject particlePrefab;
    public int MaxParticles; 
    public float particleRadius = 0.2f;

    // --- Spawning ---
    public Vector2 spawnPos = new Vector2(0,3);  // Center of the spawn region 
    public Vector2 spawnArea = new Vector2(2,2);  // Width and height of the spawn area 
    public float spawnSpacing = 0.3f;  // Distance between spawned particles

    public float dt = 0.01f;

    void Start()
    {
        SpawnInitialParticles();
    }

    void Update()
    {
        foreach (var p in particles)
        {
            p.force = new Vector2(0, -9.81f);  // Apply gravity 

            p.velocity += p.force * dt;

            p.position += p.velocity * dt;
        }
    }

    // -- Spawns a grid of particles --
    void SpawnInitialParticles()
    {
        if (particlePrefab == null)
        {
            Debug.LogError("Particle prefab not assigned");
            return;
        }

        // Calculates inital spawn corner 
        Vector2 startPos = spawnPos - spawnArea * 0.5f;
        int spawnedCount = 0;

        for (float x = 0; x < spawnArea.x && spawnedCount < MaxParticles; x += spawnSpacing)
        {
            for (float y = 0; y < spawnArea.y && spawnedCount < MaxParticles; y += spawnSpacing)
            {
                Vector2 pos = startPos + new Vector2(x,y);
                
                SpawnParticle(pos);
                spawnedCount++;

            }
        }

        Debug.Log($"Spawned {particles.Count} SPH particles");
    }

   // -- Spawn a single particle-- 
    public void SpawnParticle(Vector2 position)
    {
        // Limit check 
        if (particles.Count >= MaxParticles) return;

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
}
