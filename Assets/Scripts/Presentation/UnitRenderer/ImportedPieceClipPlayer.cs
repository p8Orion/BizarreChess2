using System;
using System.Collections.Generic;
using UnityEngine;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Plays GLB clips named Move / *_Move and Attack / *_Attack.
    /// Pieces without those clips are ignored.
    /// </summary>
    public sealed class ImportedPieceClipPlayer
    {
        public const string MoveClipName = "Move";
        public const string AttackClipName = "Attack";

        private readonly GameObject _root;
        private readonly AnimationClip _moveClip;
        private readonly AnimationClip _attackClip;
        private AnimationClip _activeClip;
        private bool _playing;
        private bool _loopWhileTraveling;
        private bool _resetWhenDone;
        private float _time;

        public bool HasMoveClip => _moveClip != null;
        public bool HasAttackClip => _attackClip != null;
        public bool IsPlaying => _playing;

        private ImportedPieceClipPlayer(GameObject root, AnimationClip moveClip, AnimationClip attackClip)
        {
            _root = root;
            _moveClip = moveClip;
            _attackClip = attackClip;
        }

        public static ImportedPieceClipPlayer TryCreate(Transform pieceRoot, GameObject modelPrefab)
        {
            if (pieceRoot == null)
                return null;

            GameObject clipRoot = FindClipRoot(pieceRoot.gameObject);
            var clips = CollectAllClips(clipRoot, modelPrefab);
            AnimationClip move = ResolveNamedClip(clips, MoveClipName);
            AnimationClip attack = ResolveNamedClip(clips, AttackClipName, allowSingleFallback: false);
            if ((move == null && attack == null) || clipRoot == null)
                return null;

            var legacy = clipRoot.GetComponent<Animation>();
            if (legacy != null)
            {
                legacy.playAutomatically = false;
                legacy.Stop();
                legacy.enabled = false;
            }

            var animator = clipRoot.GetComponent<Animator>();
            if (animator != null)
                animator.enabled = false;

            if (move != null)
                Debug.Log($"[ImportedPieceClip] {clipRoot.name} move '{move.name}' ({move.length:0.00}s)");
            if (attack != null)
                Debug.Log($"[ImportedPieceClip] {clipRoot.name} attack '{attack.name}' ({attack.length:0.00}s)");

            return new ImportedPieceClipPlayer(clipRoot, move, attack);
        }

        public void PlayMove()
        {
            Play(_moveClip, loopWhileTraveling: true, resetWhenDone: false);
        }

        public void PlayAttack()
        {
            Play(_attackClip, loopWhileTraveling: false, resetWhenDone: true);
        }

        public void NotifyTravelFinished()
        {
            _loopWhileTraveling = false;
            if (!_playing)
                return;

            if (_activeClip == null || _activeClip.length <= 0f || _time >= _activeClip.length)
                Stop(resetToStart: _resetWhenDone);
        }

        public void Stop(bool resetToStart = true)
        {
            if (_activeClip == null || _root == null)
            {
                _playing = false;
                _activeClip = null;
                return;
            }

            float sampleTime = resetToStart || _activeClip.length <= 0f ? 0f : _activeClip.length;
            _activeClip.SampleAnimation(_root, sampleTime);
            _playing = false;
            _loopWhileTraveling = false;
            _activeClip = null;
            _time = sampleTime;
        }

        public void Tick(float deltaTime)
        {
            if (!_playing || _activeClip == null || _root == null)
                return;

            float length = _activeClip.length;
            if (length <= 0f)
            {
                Stop(resetToStart: true);
                return;
            }

            _time += deltaTime;
            if (_time >= length)
            {
                if (_loopWhileTraveling)
                    _time %= length;
                else
                {
                    Stop(resetToStart: _resetWhenDone);
                    return;
                }
            }

            _activeClip.SampleAnimation(_root, _time);
        }

        private void Play(AnimationClip clip, bool loopWhileTraveling, bool resetWhenDone)
        {
            if (clip == null || _root == null)
                return;

            _activeClip = clip;
            _playing = true;
            _loopWhileTraveling = loopWhileTraveling;
            _resetWhenDone = resetWhenDone;
            _time = 0f;
            clip.SampleAnimation(_root, 0f);
        }

        private static GameObject FindClipRoot(GameObject pieceRoot)
        {
            var legacy = pieceRoot.GetComponentInChildren<Animation>(true);
            if (legacy != null)
                return legacy.gameObject;

            var animator = pieceRoot.GetComponentInChildren<Animator>(true);
            if (animator != null)
                return animator.gameObject;

            return pieceRoot.transform.childCount > 0
                ? pieceRoot.transform.GetChild(0).gameObject
                : pieceRoot;
        }

        private static List<AnimationClip> CollectAllClips(GameObject clipRoot, GameObject modelPrefab)
        {
            var clips = new List<AnimationClip>();
            CollectClips(clipRoot, clips);
            if (modelPrefab != null)
                CollectResourceClips(modelPrefab.name, clips);
            return clips;
        }

        private static void CollectClips(GameObject clipRoot, List<AnimationClip> clips)
        {
            if (clipRoot == null)
                return;

            var legacy = clipRoot.GetComponent<Animation>();
            if (legacy != null)
            {
                if (legacy.clip != null)
                    AddClip(clips, legacy.clip);

                foreach (AnimationState state in legacy)
                {
                    if (state != null)
                        AddClip(clips, state.clip);
                }
            }

            var animator = clipRoot.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                var controllerClips = animator.runtimeAnimatorController.animationClips;
                if (controllerClips != null)
                {
                    for (int i = 0; i < controllerClips.Length; i++)
                        AddClip(clips, controllerClips[i]);
                }
            }
        }

        private static void CollectResourceClips(string modelName, List<AnimationClip> clips)
        {
            if (string.IsNullOrEmpty(modelName))
                return;

            AddLoadedClips(Resources.LoadAll<AnimationClip>($"Pieces/Models/{modelName}"), clips);

            string lower = modelName.ToLowerInvariant();
            if (lower != modelName)
                AddLoadedClips(Resources.LoadAll<AnimationClip>($"Pieces/Models/{lower}"), clips);
        }

        private static void AddLoadedClips(AnimationClip[] loaded, List<AnimationClip> clips)
        {
            if (loaded == null)
                return;

            for (int i = 0; i < loaded.Length; i++)
                AddClip(clips, loaded[i]);
        }

        private static void AddClip(List<AnimationClip> clips, AnimationClip clip)
        {
            if (clip == null)
                return;

            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] == clip || clips[i].name == clip.name)
                    return;
            }

            clips.Add(clip);
        }

        private static AnimationClip ResolveNamedClip(
            List<AnimationClip> clips,
            string genericName,
            bool allowSingleFallback = true)
        {
            AnimationClip exact = null;
            AnimationClip suffix = null;
            AnimationClip contains = null;
            AnimationClip only = null;
            int count = 0;

            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clip == null)
                    continue;

                count++;
                only = clip;
                string name = clip.name;

                if (name.Equals(genericName, StringComparison.OrdinalIgnoreCase))
                    exact = clip;
                else if (name.EndsWith("_" + genericName, StringComparison.OrdinalIgnoreCase)
                         || name.EndsWith("." + genericName, StringComparison.OrdinalIgnoreCase))
                    suffix ??= clip;
                else if (name.IndexOf(genericName, StringComparison.OrdinalIgnoreCase) >= 0)
                    contains ??= clip;
            }

            if (exact != null)
                return exact;
            if (suffix != null)
                return suffix;
            if (contains != null)
                return contains;

            return allowSingleFallback && count == 1 ? only : null;
        }
    }
}
