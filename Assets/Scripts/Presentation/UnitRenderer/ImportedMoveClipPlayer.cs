using System;
using System.Collections.Generic;
using UnityEngine;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Plays a GLB clip named Move or *_Move while the piece travels.
    /// Pieces without that clip are ignored.
    /// </summary>
    public sealed class ImportedMoveClipPlayer
    {
        public const string GenericClipName = "Move";

        private readonly GameObject _root;
        private readonly AnimationClip _clip;
        private bool _playing;
        private bool _loopWhileTraveling;
        private float _time;

        public bool HasClip => _clip != null;

        private ImportedMoveClipPlayer(GameObject root, AnimationClip clip)
        {
            _root = root;
            _clip = clip;
        }

        public static ImportedMoveClipPlayer TryCreate(Transform pieceRoot, GameObject modelPrefab)
        {
            if (pieceRoot == null)
                return null;

            GameObject clipRoot = FindClipRoot(pieceRoot.gameObject);
            AnimationClip clip = FindMoveClip(clipRoot, modelPrefab);
            if (clip == null || clipRoot == null)
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

            Debug.Log($"[ImportedMoveClip] {clipRoot.name} move clip '{clip.name}' ({clip.length:0.00}s)");
            return new ImportedMoveClipPlayer(clipRoot, clip);
        }

        public void Play()
        {
            if (_clip == null || _root == null)
                return;

            _playing = true;
            _loopWhileTraveling = true;
            _time = 0f;
            _clip.SampleAnimation(_root, 0f);
        }

        public void NotifyTravelFinished()
        {
            _loopWhileTraveling = false;
            if (!_playing)
                return;

            if (_clip == null || _clip.length <= 0f || _time >= _clip.length)
                Stop(resetToStart: false);
        }

        public void Stop(bool resetToStart = true)
        {
            if (_clip == null || _root == null)
            {
                _playing = false;
                return;
            }

            _playing = false;
            _loopWhileTraveling = false;
            float sampleTime = resetToStart || _clip.length <= 0f ? 0f : _clip.length;
            _clip.SampleAnimation(_root, sampleTime);
            _time = sampleTime;
        }

        public void Tick(float deltaTime)
        {
            if (!_playing || _clip == null || _root == null)
                return;

            float length = _clip.length;
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
                    Stop(resetToStart: false);
                    return;
                }
            }

            _clip.SampleAnimation(_root, _time);
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

        private static AnimationClip FindMoveClip(GameObject clipRoot, GameObject modelPrefab)
        {
            var clips = new List<AnimationClip>();
            CollectClips(clipRoot, clips);

            if (modelPrefab != null)
                CollectResourceClips(modelPrefab.name, clips);

            return ResolveMoveClip(clips);
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

        private static AnimationClip ResolveMoveClip(List<AnimationClip> clips)
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

                if (name.Equals(GenericClipName, StringComparison.OrdinalIgnoreCase))
                    exact = clip;
                else if (name.EndsWith("_" + GenericClipName, StringComparison.OrdinalIgnoreCase)
                         || name.EndsWith("." + GenericClipName, StringComparison.OrdinalIgnoreCase))
                    suffix ??= clip;
                else if (name.IndexOf(GenericClipName, StringComparison.OrdinalIgnoreCase) >= 0)
                    contains ??= clip;
            }

            if (exact != null)
                return exact;
            if (suffix != null)
                return suffix;
            if (contains != null)
                return contains;

            return count == 1 ? only : null;
        }
    }
}
