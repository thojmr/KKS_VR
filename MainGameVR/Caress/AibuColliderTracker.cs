﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KKS_VR.Interpreters;
using UnityEngine;
using VRGIN.Core;
using Object = UnityEngine.Object;

namespace KKS_VR.Caress
{
    /// <summary>
    /// An object that tracks the set of aibu colliders that we are
    /// currently intersecting.
    /// Each instance only concerns one H scene. A fresh instance should be
    /// created for each H scene.
    /// </summary>
    internal class AibuColliderTracker
    {
        private static readonly IDictionary<string, HandCtrl.AibuColliderKind[]> aibuTagTable
            = new Dictionary<string, HandCtrl.AibuColliderKind[]>
            {
                { "mouth", new[] { HandCtrl.AibuColliderKind.mouth, HandCtrl.AibuColliderKind.reac_head } },
                { "muneL", new[] { HandCtrl.AibuColliderKind.muneL, HandCtrl.AibuColliderKind.reac_bodyup } },
                { "muneR", new[] { HandCtrl.AibuColliderKind.muneR, HandCtrl.AibuColliderKind.reac_bodyup } },
                { "kokan", new[] { HandCtrl.AibuColliderKind.kokan, HandCtrl.AibuColliderKind.reac_bodydown } },
                { "anal", new[] { HandCtrl.AibuColliderKind.anal, HandCtrl.AibuColliderKind.reac_bodydown } },
                { "siriL", new[] { HandCtrl.AibuColliderKind.siriL, HandCtrl.AibuColliderKind.reac_bodydown } },
                { "siriR", new[] { HandCtrl.AibuColliderKind.siriR, HandCtrl.AibuColliderKind.reac_bodydown } },
                { "Reaction/head", new[] { HandCtrl.AibuColliderKind.reac_head } },
                { "Reaction/bodyup", new[] { HandCtrl.AibuColliderKind.reac_bodyup } },
                { "Reaction/bodydown", new[] { HandCtrl.AibuColliderKind.reac_bodydown } },
                { "Reaction/armL", new[] { HandCtrl.AibuColliderKind.reac_armL } },
                { "Reaction/armR", new[] { HandCtrl.AibuColliderKind.reac_armR } },
                { "Reaction/legL", new[] { HandCtrl.AibuColliderKind.reac_legL } },
                { "Reaction/legR", new[] { HandCtrl.AibuColliderKind.reac_legR } }
            };

        private readonly IDictionary<Collider, ValueTuple<int /*female index*/, HandCtrl.AibuColliderKind>> _currentlyIntersecting
            = new Dictionary<Collider, ValueTuple<int, HandCtrl.AibuColliderKind>>();

        private readonly IDictionary<Collider, ValueTuple<int /*female index*/, HandCtrl.AibuColliderKind[]>> _knownColliders
            = new Dictionary<Collider, ValueTuple<int, HandCtrl.AibuColliderKind[]>>();

        private readonly Transform _referencePoint;

        public AibuColliderTracker(HSceneProc proc, Transform referencePoint)
        {
            Proc = proc;
            _referencePoint = referencePoint;

            // Populate _knwonColliders
            var lstFemale = new Traverse(proc).Field("lstFemale").GetValue<List<ChaControl>>();
            for (var i = 0; i < lstFemale.Count; i++)
            {
                var colliders = lstFemale[i].GetComponentsInChildren<Collider>(true);
                foreach (var collider in colliders)
                {
                    var aibuHit = Extensions.StripPrefix("H/Aibu/Hit/", collider.tag);
                    if (aibuHit == null) continue;

                    if (aibuTagTable.TryGetValue(aibuHit, out var kinds)) _knownColliders[collider] = ValueTuple.Create(i, kinds);
                }
            }
        }

        public HSceneProc Proc { get; }

        /// <summary>
        /// Create or destroy an AibuColliderTracker instance as necessary.
        /// </summary>
        /// <param name="prev">The current instance. May be null.</param>
        /// <param name="interpreter"></param>
        /// <returns>The new instance. May be the same instance as prev. May be null.</returns>
        public static AibuColliderTracker CreateOrDestroy(AibuColliderTracker prev, KoikatuInterpreter interpreter, Transform referencePoint)
        {
            if (interpreter.CurrentScene == KoikatuInterpreter.SceneType.HScene)
            {
                if (prev == null)
                {
                    var hSceneProc = Object.FindObjectOfType<HSceneProc>();
                    if (hSceneProc != null && hSceneProc.enabled) return new AibuColliderTracker(hSceneProc, referencePoint);
                }

                return prev;
            }

            return null;
        }

        /// <summary>
        /// If the given collider is an aibu collider, start tracking it.
        /// </summary>
        /// <param name="other"></param>
        /// <returns>Whether the current collider kind may have changed.</returns>
        public bool AddIfRelevant(Collider other)
        {
            if (!_knownColliders.TryGetValue(other, out var idx_kinds)) return false;

            var idx = idx_kinds.Item1;
            var kinds = idx_kinds.Item2;
            var hand = idx == 0 ? Proc.hand : Proc.hand1;
            var kind = kinds.Where(k => AibuKindAllowed(hand, k)).FirstOrDefault();
            if (kind != HandCtrl.AibuColliderKind.none)
            {
                _currentlyIntersecting[other] = ValueTuple.Create(idx, kind);
                return true;
            }

            return false;
        }

        /// <summary>
        /// If the given collider is currently being tracked, stop tracking it.
        /// </summary>
        /// <param name="other"></param>
        /// <returns>Whether the current collider kind may have changed.</returns>
        public bool RemoveIfRelevant(Collider other)
        {
            return _currentlyIntersecting.Remove(other);
        }

        /// <summary>
        /// Get The kind of the collider we should be interacting with.
        /// Also outputs the female who is the owner of the collider.
        /// </summary>
        public HandCtrl.AibuColliderKind GetCurrentColliderKind(out int femaleIndex)
        {
            if (_currentlyIntersecting.Count == 0)
            {
                femaleIndex = 0;
                return HandCtrl.AibuColliderKind.none;
            }

            // Only consider the colliders with the highest priority.
            var priority = _currentlyIntersecting.Values.Select(idx_kind => ColliderPriority(idx_kind.Item2)).Max();
            var refPosition = _referencePoint.position;
            var best = _currentlyIntersecting.Where(kv => ColliderPriority(kv.Value.Item2) == priority)
                .OrderBy(kv => (kv.Key.transform.position - refPosition).sqrMagnitude)
                .Select(kv => kv.Value).FirstOrDefault();
            femaleIndex = best.Item1;
            return best.Item2;
        }

        /// <summary>
        /// Return whether there is any collider we should be interacting with.
        /// This is equivalent to this.GetCurrentColliderKind() != none, but is more efficient.
        /// </summary>
        /// <returns></returns>
        public bool IsIntersecting()
        {
            return _currentlyIntersecting.Count > 0;
        }

        private static int ColliderPriority(HandCtrl.AibuColliderKind kind)
        {
            if (kind == HandCtrl.AibuColliderKind.none)
                return 0;
            if (HandCtrl.AibuColliderKind.mouth <= kind && kind < HandCtrl.AibuColliderKind.reac_head)
                return 2;
            return 1;
        }

        /// <summary>
        /// Check whether a particular body interaction is allowed.
        /// </summary>
        /// <param name="hand"></param>
        /// <param name="kind"></param>
        /// <returns></returns>
        private bool AibuKindAllowed(HandCtrl hand, HandCtrl.AibuColliderKind kind)
        {
            // It's ok to use lstHeroine[0] here because this variable is not used
            // when there are more than one heroine.
            var heroine = hand.flags.lstHeroine[0];
            var dicNowReaction = new Traverse(hand).Field("dicNowReaction").GetValue<Dictionary<int, HandCtrl.ReactionInfo>>();
            HandCtrl.ReactionInfo rinfo;
            switch (kind)
            {
                case HandCtrl.AibuColliderKind.none:
                    return true;
                case HandCtrl.AibuColliderKind.mouth:
                    return hand.nowMES.isTouchAreas[0] &&
                           (hand.flags.mode == HFlag.EMode.aibu || heroine.isGirlfriend || heroine.isKiss || heroine.denial.kiss);
                case HandCtrl.AibuColliderKind.muneL:
                    return hand.nowMES.isTouchAreas[1];
                case HandCtrl.AibuColliderKind.muneR:
                    return hand.nowMES.isTouchAreas[2];
                case HandCtrl.AibuColliderKind.kokan:
                    return hand.nowMES.isTouchAreas[3];
                case HandCtrl.AibuColliderKind.anal:
                    return hand.nowMES.isTouchAreas[4] &&
                           (hand.flags.mode == HFlag.EMode.aibu || heroine.hAreaExps[3] > 0f || heroine.denial.anal);
                case HandCtrl.AibuColliderKind.siriL:
                    return hand.nowMES.isTouchAreas[5];
                case HandCtrl.AibuColliderKind.siriR:
                    return hand.nowMES.isTouchAreas[6];
                case HandCtrl.AibuColliderKind.reac_head:
                    return dicNowReaction.TryGetValue(0, out rinfo) && rinfo.isPlay;
                case HandCtrl.AibuColliderKind.reac_bodyup:
                    return dicNowReaction.TryGetValue(1, out rinfo) && rinfo.isPlay;
                case HandCtrl.AibuColliderKind.reac_bodydown:
                    return dicNowReaction.TryGetValue(2, out rinfo) && rinfo.isPlay;
                case HandCtrl.AibuColliderKind.reac_armL:
                    return dicNowReaction.TryGetValue(3, out rinfo) && rinfo.isPlay;
                case HandCtrl.AibuColliderKind.reac_armR:
                    return dicNowReaction.TryGetValue(4, out rinfo) && rinfo.isPlay;
                case HandCtrl.AibuColliderKind.reac_legL:
                    return dicNowReaction.TryGetValue(5, out rinfo) && rinfo.isPlay;
                case HandCtrl.AibuColliderKind.reac_legR:
                    return dicNowReaction.TryGetValue(6, out rinfo) && rinfo.isPlay;
            }

            VRLog.Warn("AibuKindAllowed: undefined kind: {0}", kind);
            return false;
        }
    }
}
