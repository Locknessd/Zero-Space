using System;
using System.Linq;
using MemeArena;
using UnityEditor;
using UnityEngine;

namespace MemeArenaEditor
{
    public static partial class MemeArenaPlayCheck
    {
        static MemeArenaFloorFracture fracture;
        static int groundContacts;
        static bool floorMismatch, savedFracture;
        static float groundAt;

        static bool FloorReady()
        {
            fracture = stage.GetComponent<MemeArenaFloorFracture>();
            if (!fracture) throw new Exception("The updated stage is missing floor destruction.");
            return fracture.IsBound;
        }

        static void BeginFloorCheck()
        {
            groundContacts = 0;
            floorMismatch = false;
            savedFracture = false;
            game.battleVfx.ContactOccurred += ObserveFloor;
            var spectators = adapter.crowd.settings.spectators;
            int expected = adapter.crowd.settings.rosterId == "robinhood-2026-10-20" ?
                MemeCatalogDefinition.Load().entries.Length : 16;
            if (spectators.Length != expected || spectators.Any(value => !value.sprite || value.prefab))
                throw new Exception("Expected all imported meme sprites without old prefab overrides.");
            if (spectators.Select(value => value.sprite.texture).Distinct().Count() != 1)
                throw new Exception("The new spectators are not sharing their source atlas.");
            report.AppendLine("PASS all " + spectators.Length + " meme spectator types use the same atlas: " +
                AssetDatabase.GetAssetPath(spectators[0].sprite.texture));
            report.AppendLine("PASS pooled floor destruction subscribed before animation playback; slots=" + fracture.pool.Length);
        }

        static void ObserveFloor(BattleVfxPlayer.Impact impact)
        {
            if (impact.kind != BattleVfxPlayer.ContactKind.Ground) return;
            groundContacts++;
            groundAt = Time.time;
            if (fracture.PlayedCount != groundContacts || fracture.LastEventId != impact.eventId ||
                Mathf.Abs(fracture.LastPosition.x - impact.position.x) > .001f ||
                Mathf.Abs(fracture.LastPosition.z - impact.position.z) > .001f ||
                Mathf.Abs(fracture.LastPosition.y - (game.battleVfx.groundHeight + .018f)) > .001f)
                floorMismatch = true;
        }

        static void CaptureFloor()
        {
            if (savedFracture || fracture.ActiveCount == 0 || Time.time - groundAt < .12f || capture != null) return;
            capture = "FloorFracture";
            savedFracture = true;
        }

        static void VerifyFloorContacts()
        {
            if (groundContacts == 0 || floorMismatch || fracture.PlayedCount != groundContacts ||
                fracture.ActiveCount > fracture.pool.Length)
                throw new Exception("Floor destruction was not aligned with every real ground contact.");
            report.AppendLine("PASS floor destruction on exact contact frame and landing position; ground contacts=" +
                groundContacts + "; fracture bursts=" + fracture.PlayedCount + "; pool=" + fracture.pool.Length);
        }

        static void EndFloorCheck()
        {
            if (game && game.battleVfx) game.battleVfx.ContactOccurred -= ObserveFloor;
            fracture = null;
        }
    }
}
