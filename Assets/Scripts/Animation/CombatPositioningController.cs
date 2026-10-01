using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(12000)]
public class CombatPositioningController : MonoBehaviour
{
    public static CombatPositioningController Instance { get; private set; }
    [SerializeField] private Transform leftFighter;
    [SerializeField] private Transform rightFighter;
    [SerializeField, Min(0f)] private float moveDuration = 0.22f;
    [SerializeField] private bool faceEachOther = true;
    [SerializeField] private bool resetGroundYAfterAnim = true;
    [SerializeField] private bool lockDepthAxis = true;

    public Transform LeftFighter => leftFighter;
    public Transform RightFighter => rightFighter;
    private float _leftGround;
    private float _rightGround;
    private float _lockedDepth;
    private bool _depthCaptured;

    private void Awake()
    {
        Instance = this;
        CaptureGround();
        CaptureDepth();
    }

    public void SetFighters(Transform left, Transform right)
    {
        if (leftFighter == left && rightFighter == right) return;
        leftFighter = left;
        rightFighter = right;
        CaptureGround();
        CaptureDepth();
    }

    private void CaptureGround()
    {
        if (leftFighter != null) _leftGround = leftFighter.position.y;
        if (rightFighter != null) _rightGround = rightFighter.position.y;
    }

    private void CaptureDepth()
    {
        _depthCaptured = false;
        if (!lockDepthAxis || leftFighter == null || rightFighter == null) return;

        // Both fighters start on the same lane in BattleScene. Use their midpoint so a
        // small prefab offset cannot make one fighter inherit the other fighter's Z.
        _lockedDepth = (leftFighter.position.z + rightFighter.position.z) * 0.5f;
        _depthCaptured = true;
        ConstrainDepth();
    }

    private void LateUpdate()
    {
        // Retargeting and animation callbacks run before this controller. Applying the
        // constraint last prevents authored root travel or positioning from sliding a
        // fighter off the battle lane for a frame.
        ConstrainDepth();
    }

    private void ConstrainDepth()
    {
        if (!lockDepthAxis || !_depthCaptured) return;
        LockDepth(leftFighter);
        LockDepth(rightFighter);
    }

    public void ConstrainDepthNow() => ConstrainDepth();

    private void LockDepth(Transform fighter)
    {
        if (fighter == null) return;
        Vector3 position = fighter.position;
        if (Mathf.Abs(position.z - _lockedDepth) <= 0.00001f) return;
        position.z = _lockedDepth;
        fighter.position = position;
    }


    public Transform GetFighter(PlayerUI.Side side) =>
        side == PlayerUI.Side.Left ? leftFighter : rightFighter;

    public IEnumerator MoveIntoRange(CharacterCombat attacker, CharacterCombat receiver, float range)
    {
        if (attacker == null || receiver == null || range <= 0f) yield break;
        Transform source = attacker.Animator.transform;
        Transform target = receiver.Animator.transform;
        if (lockDepthAxis && _depthCaptured)
        {
            LockDepth(source);
            LockDepth(target);
        }

        // Battle is a side-on lane. The attack range is authored as the separation
        // between the two fighter roots on X, so vertical animation offsets and the
        // locked Z depth must not change the distance used to start an attack.
        float side = Mathf.Sign(source.position.x - target.position.x);
        if (Mathf.Abs(side) < 0.5f) side = source.position.x >= target.position.x ? 1f : -1f;
        Vector3 destination = target.position;
        destination.x = target.position.x + side * range;
        destination.y = source.position.y;
        if (lockDepthAxis && _depthCaptured) destination.z = _lockedDepth;
        Face(source, target);
        Face(target, source);

        float currentSeparation = Mathf.Abs(source.position.x - target.position.x);
        if (Mathf.Abs(currentSeparation - range) <= 0.001f)
        {
            source.position = destination;
            yield break;
        }

        Vector3 origin = source.position;
        attacker.Move(1f);
        try
        {
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                if (attacker == null || receiver == null) yield break;
                elapsed += Time.deltaTime;
                source.position = Vector3.Lerp(origin, destination,
                    moveDuration > 0f ? Mathf.SmoothStep(0f, 1f, elapsed / moveDuration) : 1f);
                yield return null;
            }
            source.position = destination;
        }
        finally
        {
            if (attacker != null) attacker.Move(0f);
        }
    }

    public void FinishExchange(CharacterCombat left, CharacterCombat right)
    {
        Correct(left, _leftGround);
        Correct(right, _rightGround);
        if (left != null && right != null)
        {
            if (!left.IsDead) Face(left.Animator.transform, right.Animator.transform);
            if (!right.IsDead) Face(right.Animator.transform, left.Animator.transform);
        }
    }

    private void Correct(CharacterCombat combat, float ground)
    {
        if (combat == null) return;
        Vector3 position = combat.Animator.transform.position;
        if (resetGroundYAfterAnim && !combat.IsDead) position.y = ground;
        if (lockDepthAxis && _depthCaptured) position.z = _lockedDepth;
        combat.Animator.transform.position = position;
    }

    private void Face(Transform source, Transform target)
    {
        if (!faceEachOther) return;
        Vector3 direction = target.position - source.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) source.rotation = Quaternion.LookRotation(direction);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
