using UnityEngine;

public class FluidParticle : MonoBehaviour
{
    public Vector2 velocity;
    public Vector2 force;
    public float density;
    public float pressure; 
    public float mass;
    public float smoothingRadius;

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
}
