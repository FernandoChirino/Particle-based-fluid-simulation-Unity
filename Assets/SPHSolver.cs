using System.Collections.Generic;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using System;
using UnityEngine.InputSystem;

public class SPHSolver : MonoBehaviour
{
    [Header("Simulation Parameters")]
    public float gravitiy;
    public float smoothingRadius = 2.0f;
    public float targetDensity;
    public float pressureMultiplier;
    public float viscosityStrength = 0.05f;
    private float particleMass = 1.0f;

    [Header("Collision")]
    public Vector2 BoundsSize; 
    public float CollisionDamping; 
    private LineRenderer lr;

    [Header("Particle Setup")]
    public int numParticles; 
    public GameObject particlePrefab;
    public float particleRadius = 0.2f;
    public float particleSpacing;
    public float maxSpeed = 10f;
    private List<FluidParticle> particles = new List<FluidParticle>(); // Dynamic list 

    [Header("Interaction")]
    public float interactionRadius;
    public float interactionStrength;

    private struct SpatialEntry
    {
        public int particleIndex;
        public uint cellKey;
    }
       
    private SpatialEntry[] spatialLookup;
    private int[] startIndices;
    private Vector2[] predictedPositions;
    private readonly int[] cellOffsets = new int[]
    {
        -1, -1,  // bottom-left
        -1,  0,  // left
        -1,  1,  // top-left
         0, -1,  // bottom
         0,  0,  // center
         0,  1,  // top
         1, -1,  // bottom-right
         1,  0,  // right
         1,  1   // top-right
    };

    public static SPHSolver Instance;

    private Camera mainCamera; 

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
        mainCamera = Camera.main; 
        SpawnInitialParticles();
        InitializeSpatialHashing();
    }

    void Update()
    {

        HandleMouseInteraction(Time.deltaTime);
        // Apply gravity and predict next positions (look ahead)
        for (int i = 0; i < particles.Count; i++)
        {
            Vector2 gravity = new Vector2(0, -gravitiy);
            particles[i].velocity += gravity * Time.deltaTime;
            predictedPositions[i] = particles[i].position + particles[i].velocity * Time.deltaTime / 120f;
        }

        // Update spatial lookup with predicted positions
        UpdateSpatialLookup(predictedPositions);
        
        

        // Calculate densities using predicted positions
        CalculateDensities();

        for (int i = 0; i < particles.Count; i++)
        {
            Vector2 pressureForce = CalculatePressureForce(i);
            Vector2 viscosityForce = CalculateViscosityForce(i);

            Vector2 Acceleration = (pressureForce + viscosityForce) / particles[i].density;
            particles[i].velocity += Acceleration * Time.deltaTime;

            // Limit max speed
            float speed = particles[i].velocity.magnitude;
            if (speed > maxSpeed)
            {
                particles[i].velocity = (particles[i].velocity / speed) * maxSpeed;
            }
        }

        // Update positions and resolve collisions
        for (int i = 0; i < particles.Count; i++)
        {
            particles[i].position += particles[i].velocity * Time.deltaTime;
            ResolveCollisions(particles[i]);
        }

    }

    void InitializeSpatialHashing()
    {
        spatialLookup = new SpatialEntry[numParticles];
        startIndices = new int[numParticles];
        predictedPositions = new Vector2[numParticles];
    }

    void UpdateSpatialLookup(Vector2[] positions)
    {
        // Create spatial lookup entries
        for (int i = 0; i < particles.Count; i++)
        {
            (int cellX, int cellY) = PositionToCellCoord(positions[i], smoothingRadius);
            uint cellKey = GetKeyFromHash(HashCell(cellX, cellY));
            
            spatialLookup[i] = new SpatialEntry
            {
                particleIndex = i,
                cellKey = cellKey
            };
            startIndices[i] = int.MaxValue;
        }

        // Sort by cell key
        Array.Sort(spatialLookup, (a, b) => a.cellKey.CompareTo(b.cellKey));

        // Calculate start indices for each unique cell
        for (int i = 0; i < particles.Count; i++)
        {
            uint key = spatialLookup[i].cellKey;
            uint keyPrev = i == 0 ? uint.MaxValue : spatialLookup[i - 1].cellKey;
            
            if (key != keyPrev)
            {
                startIndices[key] = i;
            }
        }
    }

    (int, int) PositionToCellCoord(Vector2 position, float radius)
    {
        int cellX = (int)(position.x / radius);
        int cellY = (int)(position.y / radius);
        return (cellX, cellY);
    }

    uint HashCell(int cellX, int cellY)
    {
        uint a = (uint)cellX * 15823;
        uint b = (uint)cellY * 9737333;
        return a + b;
    }

    uint GetKeyFromHash(uint hash)
    {
        return hash % (uint)numParticles;
    }

    void ForeachPointWithinRadius(Vector2 samplePoint, System.Action<int> callback)
    {
        (int centreX, int centreY) = PositionToCellCoord(samplePoint, smoothingRadius);
        float sqrRadius = smoothingRadius * smoothingRadius;

        for (int i = 0; i < cellOffsets.Length; i += 2)
        {
            int offsetX = cellOffsets[i];
            int offsetY = cellOffsets[i + 1];
            
            uint key = GetKeyFromHash(HashCell(centreX + offsetX, centreY + offsetY));
            int cellStartIndex = startIndices[key];

            // Skip if cell is empty (CRITICAL FIX!)
            if (cellStartIndex == int.MaxValue) continue;

            for (int j = cellStartIndex; j < spatialLookup.Length; j++)
            {
                if (spatialLookup[j].cellKey != key) break;

                int particleIndex = spatialLookup[j].particleIndex;
                
                // Use predicted positions! (CRITICAL FIX!)
                float sqrDst = (predictedPositions[particleIndex] - samplePoint).sqrMagnitude;

                if (sqrDst <= sqrRadius)
                {
                    callback(particleIndex);
                }
            }
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
            //Debug.Log($"X Collision! pos.x={pos.x}, halfBounds={halfBoundsSize.x}");
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
        for (int i = 0; i < particles.Count; i++)
        {
            float density = 0f;

            ForeachPointWithinRadius(predictedPositions[i], (particleIndex) =>
            {
                float distance = Vector2.Distance(predictedPositions[i], predictedPositions[particleIndex]);
                float influence = SmoothingKernel(smoothingRadius, distance);
                density += particleMass * influence;
            });
            
            particles[i].density = Mathf.Max(density, 0.0001f);
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
    
    float ViscosityKernel(float radius, float distance)
    {
        if (distance >= radius) return 0f;
        
        float v = radius * radius - distance * distance;
        float volume = (Mathf.PI * Mathf.Pow(radius, 6)) / 32f;
        return v * v * v / volume;
    }

    Vector2 CalculatePressureForce(int particleIndex)
    {
        Vector2 pressureForce = Vector2.zero;
        FluidParticle particle = particles[particleIndex];

        ForeachPointWithinRadius(predictedPositions[particleIndex], (otherIndex) =>
        {
            if (particleIndex == otherIndex) return;
        
            Vector2 offset = predictedPositions[otherIndex] - predictedPositions[particleIndex];
            float distance = offset.magnitude;

            if (distance < 0.0001f)
            {
                offset = UnityEngine.Random.insideUnitCircle.normalized * 0.0001f;
                distance = 0.0001f;
            }
            
            Vector2 direction = offset / distance;

            if (particles[otherIndex].density > 0)
            {
                float slope = SmoothingKernelDerivative(smoothingRadius, distance);
                float pressure = ConvertDensityToPressure(particle.density);
                float otherPressure = ConvertDensityToPressure(particles[otherIndex].density);
                float sharedPressure = (pressure + otherPressure) / 2f;
                
           
                pressureForce += direction * (sharedPressure * slope) * particleMass / particles[otherIndex].density;
            }
        });
        return pressureForce;
    }
    Vector2 CalculateViscosityForce(int particleIndex)
    {
        Vector2 viscosityForce = Vector2.zero;
        FluidParticle particle = particles[particleIndex];

        ForeachPointWithinRadius(predictedPositions[particleIndex], (otherIndex) =>
        {
            if (particleIndex == otherIndex) return;
            
            float distance = Vector2.Distance(predictedPositions[particleIndex], predictedPositions[otherIndex]);
            
            if (distance > 0.0001f)
            {
                float influence = ViscosityKernel(smoothingRadius, distance);
                Vector2 velocityDiff = particles[otherIndex].velocity - particle.velocity;
                
                viscosityForce += velocityDiff * influence * viscosityStrength;
            }
        });

        return viscosityForce;
    }

    float ConvertDensityToPressure(float density)
    {
        float densityErr = density - targetDensity;
        float pressure = densityErr * pressureMultiplier;
        return pressure;
    }

    void HandleMouseInteraction(float deltaTime)
    {
        var mouse = Mouse.current;
        // Left mouse button - attract particles
        if (mouse.leftButton.isPressed)
        {
            Vector2 mouseScreenPos = mouse.position.ReadValue();
            Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, 0));
            Vector2 mousePos2D = new Vector2(mouseWorldPos.x, mouseWorldPos.y);
            
            ApplyInteractionForce(mousePos2D, interactionStrength, deltaTime);
        }
        
        // Right mouse button - repel particles
        if (mouse.rightButton.isPressed)
        {
            Vector2 mouseScreenPos = mouse.position.ReadValue();
            Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, 0));
            Vector2 mousePos2D = new Vector2(mouseWorldPos.x, mouseWorldPos.y);
            
            ApplyInteractionForce(mousePos2D, -interactionStrength, deltaTime);
        }
    }

    void ApplyInteractionForce(Vector2 interactionPoint, float strength, float deltaTime)
    {
        int affectedCount = 0;
        
        // Calculate how many cells we need to check based on interaction radius
        int cellRadius = Mathf.CeilToInt(interactionRadius / smoothingRadius);
        float sqrRadius = interactionRadius * interactionRadius;
        
        (int centreX, int centreY) = PositionToCellCoord(interactionPoint, smoothingRadius);

        // Check all cells in a square around the center
        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                uint key = GetKeyFromHash(HashCell(centreX + x, centreY + y));
                int cellStartIndex = startIndices[key];

                if (cellStartIndex == int.MaxValue) continue;

                for (int j = cellStartIndex; j < particles.Count; j++)
                {
                    if (spatialLookup[j].cellKey != key) break;

                    int particleIndex = spatialLookup[j].particleIndex;
                    Vector2 offset = particles[particleIndex].position - interactionPoint;
                    float sqrDst = offset.sqrMagnitude;

                    if (sqrDst <= sqrRadius)
                    {
                        if (sqrDst < 0.0001f) sqrDst = 0.0001f;
                        
                        float distance = Mathf.Sqrt(sqrDst);
                        Vector2 direction = offset / distance;
                        
                        // Stronger falloff
                        float falloff = (interactionRadius - distance) / interactionRadius;
                        falloff = falloff * falloff;
                        
                        Vector2 force = direction * strength * falloff;
                        
                        particles[particleIndex].velocity += force * deltaTime;
                        affectedCount++;
                    }
                }
            }
        }
        
        Debug.Log($"Affected {affectedCount} particles at {interactionPoint}");
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