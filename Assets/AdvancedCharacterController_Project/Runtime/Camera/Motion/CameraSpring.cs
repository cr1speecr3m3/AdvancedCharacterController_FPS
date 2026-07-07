using UnityEngine;

public class CameraSpring : MonoBehaviour
{
    [Min(0.01f)]
    [SerializeField] private float halfLife = 0.075f;
    [Space]
    [SerializeField] private float frequency = 18f;
    [Space]
    [SerializeField] private float angularDisplacement = 2f;
    [SerializeField] private float linearDisplacement = 0.05f;
    private Vector3 _springPosition;
    private Vector3 _springVelocity;
    public void Initialize()
    {
        _springPosition = transform.position;
        _springVelocity = Vector3.zero;
    }

    public void UpdateSpring(float deltaTime, Vector3 upDirection)
    {
        transform.localPosition = Vector3.zero;
        Spring(ref _springPosition, ref _springVelocity, transform.position, halfLife, frequency, deltaTime);

        var localSpringPosition = _springPosition - transform.position;
        var springHeight = Vector3.Dot(localSpringPosition, upDirection);

        transform.localEulerAngles = new Vector3(-springHeight * angularDisplacement, 0f, 0f);
        transform.localPosition = localSpringPosition * linearDisplacement;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, _springPosition);
        Gizmos.DrawSphere(_springPosition, 0.1f);
    }

    public static void Spring(ref Vector3 current, ref Vector3 velocity, Vector3 target, float dampingRatio, float angularFrequency, float timeStep)
    {
        float f = 1.0f + 2.0f * timeStep * dampingRatio * angularFrequency;
        float oo = angularFrequency * angularFrequency;
        float hoo = timeStep * oo;
        float hhoo = timeStep * hoo;
        float detInv = 1.0f / (f + hhoo);
        Vector3 detX = f * current + timeStep * velocity + hhoo * target;
        Vector3 detV = velocity + hoo * (target - current);
        current = detX * detInv;
        velocity = detV * detInv;
    }
}
