using HarmonyLib;
using UnityEngine;

namespace GameMod.Hooks
{
    // OnCollisionEnter, not ProcessCollision: only the Collision carries the contact point, and a
    // bouncing reflex has already moved off the wall by the time its transform is read
    [HarmonyPatch(typeof(Projectile), "OnCollisionEnter")]
    class Projectile_OnCollisionEnter
    {
        static void Prefix(Projectile __instance, Collision collision)
        {
            if (collision != null && collision.gameObject != null && collision.contacts != null && collision.contacts.Length > 0)
                ImpactMarks.OnProjectileHit(__instance, collision.gameObject, collision.contacts[0].point, collision.contacts[0].normal);
        }
    }
}
