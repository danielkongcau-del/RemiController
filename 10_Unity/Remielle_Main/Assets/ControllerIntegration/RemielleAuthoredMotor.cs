using System;
using UnityEngine;
using nickmaltbie.OpenKCC.Character;

namespace Remielle.Controller
{
    [RequireComponent(typeof(KCCMovementEngine))]
    public sealed class RemielleAuthoredMotor : MonoBehaviour
    {
        KCCMovementEngine engine;
        long lastSample = long.MinValue;
        public Vector3 LastRequestedDelta { get; private set; }
        public Vector3 LastAppliedDelta { get; private set; }

        // The pose adapter must remove this delta from the visual root. The
        // fourteen Animator scalar channels are not another displacement source.
        public void ApplyAuthoredDelta(long sampleSequence, Vector3 worldDelta)
        {
            if (sampleSequence <= lastSample) throw new InvalidOperationException("Root displacement was already consumed or is out of order");
            if (!float.IsFinite(worldDelta.x) || !float.IsFinite(worldDelta.y) || !float.IsFinite(worldDelta.z))
                throw new ArgumentException("Nonfinite authored displacement");
            if (!engine) engine = GetComponent<KCCMovementEngine>();
            Vector3 before = transform.position;
            engine.MovePlayer(worldDelta);
            lastSample = sampleSequence;
            LastRequestedDelta = worldDelta;
            LastAppliedDelta = transform.position - before;
        }
    }
}
