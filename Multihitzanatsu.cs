using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;

namespace MultiHitZandatsu
{
    public class ZandatsuMod : ThunderScript
    {
        [ModOption(name = "Ударов для ослабления/разреза", tooltip = "Сколько одинаковых ударов нужно нанести по одной линии", valueSourceName = nameof(HitCountValues), defaultValueIndex = 2)]
        public static int RequiredHits = 3;

        [ModOption(name = "Режим финала", tooltip = "Резать сразу последним ударом или разрешить разорвать руками", valueSourceName = nameof(FinishModeValues), defaultValueIndex = 1)]
        public static bool RequireHandTear = true;

        [ModOption(name = "Сила рывка руками", tooltip = "Насколько резко нужно развести руки для разрыва", valueSourceName = nameof(TearSpeedValues), defaultValueIndex = 1)]
        public static float MinTearSpeed = 1.8f;

        [ModOption(name = "Допуск угла (градусы)", tooltip = "Насколько сильно может отличаться угол следующего удара", valueSourceName = nameof(AngleToleranceValues), defaultValueIndex = 2)]
        public static float MaxAngleDifference = 20f;

        [ModOption(name = "Допуск смещения (см)", tooltip = "Максимальное расстояние от первой линии разреза", valueSourceName = nameof(DistanceToleranceValues), defaultValueIndex = 1)]
        public static float MaxDistanceCm = 10f;

        public static ModOptionInt[] HitCountValues() => new[]
        {
            new ModOptionInt("1 удар", 1),
            new ModOptionInt("2 удара", 2),
            new ModOptionInt("3 удара", 3),
            new ModOptionInt("4 удара", 4),
            new ModOptionInt("5 ударов", 5)
        };

        public static ModOptionBool[] FinishModeValues() => new[]
        {
            new ModOptionBool("Сразу резать мечом", false),
            new ModOptionBool("Разорвать руками после ударов", true)
        };

        public static ModOptionFloat[] TearSpeedValues() => new[]
        {
            new ModOptionFloat("Легко (1.2 м/с)", 1.2f),
            new ModOptionFloat("Средне (1.8 м/с)", 1.8f),
            new ModOptionFloat("Тяжело (2.5 м/с)", 2.5f)
        };

        public static ModOptionFloat[] AngleToleranceValues() => new[]
        {
            new ModOptionFloat("10° (Строго)", 10f),
            new ModOptionFloat("15°", 15f),
            new ModOptionFloat("20° (Нормально)", 20f),
            new ModOptionFloat("35° (Легко)", 35f)
        };

        public static ModOptionFloat[] DistanceToleranceValues() => new[]
        {
            new ModOptionFloat("5 см", 5f),
            new ModOptionFloat("10 см", 10f),
            new ModOptionFloat("20 см", 20f)
        };

        private class CutProgress
        {
            public Transform ReferenceBone;
            public Vector3 LocalPoint;
            public Vector3 LocalNormal;
            public int CurrentHits;
            public float LastHitTime;
            public bool IsReadyToTear;
        }

        private Dictionary<Creature, CutProgress> activeCuts = new Dictionary<Creature, CutProgress>();

        public override void ScriptEnable()
        {
            base.ScriptEnable();
            EventManager.onCreatureHit += OnCreatureHit;
            EventManager.onCreatureKill += OnCreatureKill;
        }

        public override void ScriptDisable()
        {
            base.ScriptDisable();
            EventManager.onCreatureHit -= OnCreatureHit;
            EventManager.onCreatureKill -= OnCreatureKill;
        }

        private void OnCreatureKill(Creature creature, Player player, CollisionInstance collisionInstance, EventTime eventTime)
        {
            if (activeCuts.ContainsKey(creature))
                activeCuts.Remove(creature);
        }

        private void OnCreatureHit(Creature creature, CollisionInstance collision, EventTime eventTime)
        {
            if (eventTime != EventTime.OnStart || creature == null || creature.isPlayer)
                return;

            if (collision.damageStruct.damageType != DamageType.Slash)
                return;

            Vector3 hitPoint = collision.contactPoint;
            Vector3 bladeForward = collision.sourceColliderGroup.transform.up;
            Vector3 swingDirection = collision.impactVelocity.normalized;
            Vector3 cutNormal = Vector3.Cross(bladeForward, swingDirection).normalized;
            if (cutNormal == Vector3.zero) return;

            Transform hitBone = collision.targetCollider.transform;

            if (!activeCuts.TryGetValue(creature, out CutProgress progress) || (!progress.IsReadyToTear && Time.time - progress.LastHitTime > 10f))
            {
                progress = new CutProgress
                {
                    ReferenceBone = hitBone,
                    LocalPoint = hitBone.InverseTransformPoint(hitPoint),
                    LocalNormal = hitBone.InverseTransformDirection(cutNormal),
                    CurrentHits = 1,
                    LastHitTime = Time.time,
                    IsReadyToTear = false
                };
                activeCuts[creature] = progress;
                CheckCutCompletion(creature, progress);
                return;
            }

            if (progress.IsReadyToTear) return;

            Vector3 savedWorldPoint = progress.ReferenceBone.TransformPoint(progress.LocalPoint);
            Vector3 savedWorldNormal = progress.ReferenceBone.TransformDirection(progress.LocalNormal);

            float angleDiff = Vector3.Angle(savedWorldNormal, cutNormal);
            if (angleDiff > 90f) angleDiff = 180f - angleDiff;

            Plane cutPlane = new Plane(savedWorldNormal, savedWorldPoint);
            float distanceMeters = Mathf.Abs(cutPlane.GetDistanceToPoint(hitPoint));

            if (angleDiff <= MaxAngleDifference && distanceMeters <= (MaxDistanceCm / 100f))
            {
                progress.CurrentHits++;
                progress.LastHitTime = Time.time;
                CheckCutCompletion(creature, progress);
            }
            else
            {
                progress.ReferenceBone = hitBone;
                progress.LocalPoint = hitBone.InverseTransformPoint(hitPoint);
                progress.LocalNormal = hitBone.InverseTransformDirection(cutNormal);
                progress.CurrentHits = 1;
                progress.LastHitTime = Time.time;
            }
        }

        private void CheckCutCompletion(Creature creature, CutProgress progress)
        {
            if (progress.CurrentHits < RequiredHits) return;

            Vector3 worldPoint = progress.ReferenceBone.TransformPoint(progress.LocalPoint);
            Vector3 worldNormal = progress.ReferenceBone.TransformDirection(progress.LocalNormal);

            if (RequireHandTear)
            {
                progress.IsReadyToTear = true;
                creature.ragdoll.SetState(Ragdoll.State.Destabilized);
            }
            else
            {
                ExecuteZandatsuSlice(creature, worldPoint, worldNormal);
                activeCuts.Remove(creature);
            }
        }

        public override void ScriptUpdate()
        {
            base.ScriptUpdate();

            if (activeCuts.Count == 0 || Player.local == null) return;

            RagdollHand leftHand = Player.local.handLeft?.ragdollHand;
            RagdollHand rightHand = Player.local.handRight?.ragdollHand;
            if (leftHand == null || rightHand == null) return;

            HandleRagdoll leftGrab = leftHand.grabbedHandle as HandleRagdoll;
            HandleRagdoll rightGrab = rightHand.grabbedHandle as HandleRagdoll;
            if (leftGrab == null || rightGrab == null) return;

            Creature grabbedCreature = leftGrab.ragdollPart.ragdoll.creature;
            if (grabbedCreature == null || grabbedCreature != rightGrab.ragdollPart.ragdoll.creature) return;

            if (activeCuts.TryGetValue(grabbedCreature, out CutProgress progress) && progress.IsReadyToTear)
            {
                Vector3 planePoint = progress.ReferenceBone.TransformPoint(progress.LocalPoint);
                Vector3 planeNormal = progress.ReferenceBone.TransformDirection(progress.LocalNormal);
                Plane cutPlane = new Plane(planeNormal, planePoint);

                bool leftSide = cutPlane.GetSide(leftHand.transform.position);
                bool rightSide = cutPlane.GetSide(rightHand.transform.position);

                if (leftSide != rightSide)
                {
                    Vector3 handsVector = (rightHand.transform.position - leftHand.transform.position).normalized;
                    Vector3 relativeVelocity = rightHand.Velocity() - leftHand.Velocity();
                    float pullApartSpeed = Vector3.Dot(relativeVelocity, handsVector);

                    if (pullApartSpeed >= MinTearSpeed)
                    {
                        leftHand.UnGrab(false);
                        rightHand.UnGrab(false);

                        ExecuteZandatsuSlice(grabbedCreature, planePoint, planeNormal);
                        activeCuts.Remove(grabbedCreature);
                    }
                }
            }
        }

        private void ExecuteZandatsuSlice(Creature creature, Vector3 planePoint, Vector3 planeNormal)
        {
            Debug.Log($"[MultiHitZandatsu] Враг разрезан! Точка: {planePoint}");
        }
    }
}

