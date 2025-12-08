using UnityEngine;
using UnityEditor;

public class FluidParticle : MonoBehaviour
{
    public Vector2 velocity;
    public Vector2 force;
    public float density;
    public float pressure; 
    public float mass;
    

    public Vector2 position
    {
        get => transform.position;
        set => transform.position = value;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        position = transform.position; 
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnDrawGizmos()
    {
        float smoothingRadius = SPHSolver.Instance.smoothingRadius;
        // Outer faint “blur”
        Handles.color = new Color(0f, 0.5f, 1f, 0.08f);
        Handles.DrawSolidDisc(transform.position, Vector3.back, smoothingRadius);

        // Middle stronger layer
        Handles.color = new Color(0f, 0.5f, 1f, 0.12f);
        Handles.DrawSolidDisc(transform.position, Vector3.back, smoothingRadius * 0.6f);

        // Inner core
        Handles.color = new Color(0f, 0.5f, 1f, 0.2f);
        Handles.DrawSolidDisc(transform.position, Vector3.back, smoothingRadius * 0.3f);
    }
}
