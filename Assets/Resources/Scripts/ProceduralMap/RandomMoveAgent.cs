using UnityEngine;

public class RandomMoveAgent : MonoBehaviour
{
    [Header("Ground Reference")]
    [Tooltip("ลาก GroundPlaneGenerator ของพื้นที่ agent เดิน")]
    public GroundPlaneGenerator ground;
    [Tooltip("เว้นระยะจากขอบพื้น กันเดินหลุดขอบ")]
    public float edgePadding = 1f;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float rotationSpeed = 10f;
    public float waypointReachDistance = 0.3f;

    [Header("Wait At Waypoint")]
    public float minWaitTime = 0.5f;
    public float maxWaitTime = 2f;

    private Vector3 _targetPosition;
    private float _waitTimer;
    private bool _isWaiting;

    private void Start() => PickNewTarget();

    private void Update()
    {
        if (_isWaiting)
        {
            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
            {
                _isWaiting = false;
                PickNewTarget();
            }
            return;
        }

        Vector3 toTarget = _targetPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude <= waypointReachDistance * waypointReachDistance)
        {
            _isWaiting = true;
            _waitTimer = Random.Range(minWaitTime, maxWaitTime);
            return;
        }

        Vector3 moveDir = toTarget.normalized;
        transform.position += moveDir * moveSpeed * Time.deltaTime;

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void PickNewTarget()
    {
        if (ground == null)
        {
            _targetPosition = transform.position;
            return;
        }

        Bounds bounds = ground.GetWorldBounds();
        float halfW = Mathf.Max(0f, bounds.size.x * 0.5f - edgePadding);
        float halfL = Mathf.Max(0f, bounds.size.z * 0.5f - edgePadding);

        float x = Random.Range(-halfW, halfW);
        float z = Random.Range(-halfL, halfL);

        _targetPosition = bounds.center + new Vector3(x, 0f, z);
        _targetPosition.y = transform.position.y;
    }
}