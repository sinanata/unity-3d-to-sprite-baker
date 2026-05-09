using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpriteBakerDemo
{
    /// <summary>
    /// Four-card catalog: one shared Kenney AC2 rig, four CC0 skins. Bake
    /// key combines (skin × frame size × FPS × yaw count). All assets CC0
    /// — see <c>Assets/Demo/Resources/Models/CREDITS.txt</c>.
    /// </summary>
    public static class DemoCharacterCatalog
    {
        public class Definition
        {
            public string DisplayName;
            public string SkinResourcePath;
            public string DescriptionLine;
        }

        public const string CharacterPrefabPath  = "Models/characterMedium";
        public const string ControllerPath       = "Models/CharacterMediumController";
        public const string AnimIdlePath         = "Models/Animations/idle";
        public const string AnimRunPath          = "Models/Animations/run";
        public const string AnimJumpPath         = "Models/Animations/jump";

        public const int   DefaultFramePixelSize = 128;
        public const int   DefaultFrameRate      = 12;

        // 8 yaws at 45° bins ≈ 18 MB per character, ~75 MB for four cards.
        public const int   YawCount              = 8;

        // Lifts the prefab's ~0.7m render height to ~0.9m for the orbit.
        // Do NOT touch the prefab's localRotation — Kenney AC2 ships
        // bakeAxisConversion=0 so the Z-up→Y-up correction lives there.
        public const float LiveScale = 1.3f;

        public const int RowIdle = 0;
        public const int RowRun  = 1;
        public const int RowJump = 2;

        public static List<Definition> GetDefinitions()
        {
            return new List<Definition>
            {
                new Definition
                {
                    DisplayName = "Skater · M",
                    SkinResourcePath = "Models/Skins/skaterMaleA",
                    DescriptionLine = "Default skin",
                },
                new Definition
                {
                    DisplayName = "Skater · F",
                    SkinResourcePath = "Models/Skins/skaterFemaleA",
                    DescriptionLine = "Skin variant",
                },
                new Definition
                {
                    DisplayName = "Criminal",
                    SkinResourcePath = "Models/Skins/criminalMaleA",
                    DescriptionLine = "Skin variant",
                },
                new Definition
                {
                    DisplayName = "Cyborg",
                    SkinResourcePath = "Models/Skins/cyborgFemaleA",
                    DescriptionLine = "Skin variant",
                },
            };
        }

        /// <summary>
        /// Content-derived bake key — stable across editor reloads, so
        /// re-entering Play mode reuses the cached atlas. Distinct per
        /// (skin × frame size × FPS × yaw count × body lit/unlit).
        /// </summary>
        public static int BuildBakeKey(Definition def, int framePixelSize, int frameRate, int yawCount, bool bodyLit)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (def?.SkinResourcePath?.GetHashCode() ?? 0);
                h = h * 31 + framePixelSize;
                h = h * 31 + frameRate;
                h = h * 31 + yawCount;
                h = h * 31 + (bodyLit ? 1 : 0);
                return h;
            }
        }

        /// <summary>
        /// Pick a clip from a multi-clip FBX, skipping Kenney AC2's
        /// "Root|0.Targeting Pose" static sub-clip. Match by keyword first,
        /// then any non-pose clip, then anything.
        /// </summary>
        public static AnimationClip LoadAnimationByKeyword(string fbxResourcePath, string keyword)
        {
            var clips = Resources.LoadAll<AnimationClip>(fbxResourcePath);
            if (clips == null || clips.Length == 0) return null;

            foreach (var c in clips)
            {
                if (c == null) continue;
                if (c.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
            }

            foreach (var c in clips)
            {
                if (c == null) continue;
                if (c.name.IndexOf("Targeting", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (c.name.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return c;
            }

            return clips[0];
        }
    }
}
