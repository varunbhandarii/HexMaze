using System;
using System.Reflection;
using UnityEngine;

public class LocomotionModeManager : MonoBehaviour
{
    public Transform xrOriginRoot;
    public bool enableTeleport;
    public bool keepTurning = true;

    [Range(0.2f, 2f)] public float moveSpeed = 1.15f;
    [Range(15f, 120f)] public float turnSpeed = 45f;

    void Start()
    {
        if (xrOriginRoot == null)
        {
            var go = GameObject.Find("XR Origin (VR)");
            xrOriginRoot = go != null ? go.transform : null;
        }
        Apply();
    }

    public void Apply()
    {
        if (xrOriginRoot == null) return;

        foreach (var b in xrOriginRoot.GetComponentsInChildren<Behaviour>(true))
        {
            string n = b.GetType().Name;

            if (n.Contains("ContinuousMoveProvider") || n.Contains("DynamicMoveProvider"))
            {
                SetFloat(b, moveSpeed, "moveSpeed", "m_MoveSpeed");
                b.enabled = true;
            }
            else if (n.Contains("ContinuousTurnProvider") || n.Contains("SnapTurnProvider"))
            {
                SetFloat(b, turnSpeed, "turnSpeed", "m_TurnSpeed");
                b.enabled = keepTurning;
            }
            else if (IsTeleportThing(n, b.gameObject.name))
            {
                b.enabled = enableTeleport;
            }
        }
    }

    static bool IsTeleportThing(string typeName, string objName)
    {
        return typeName.Contains("TeleportationProvider")
            || typeName.Contains("TeleportationArea")
            || typeName.Contains("TeleportationAnchor")
            || (typeName.Contains("XRRayInteractor") && objName.Contains("Teleport"))
            || (typeName.Contains("XRInteractorLineVisual") && objName.Contains("Teleport"));
    }

    static void SetFloat(Behaviour b, float value, params string[] names)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type t = b.GetType(); t != null; t = t.BaseType)
        {
            foreach (var name in names)
            {
                var p = t.GetProperty(name, flags);
                if (p != null && p.CanWrite && p.PropertyType == typeof(float)) { p.SetValue(b, value); return; }
                var f = t.GetField(name, flags);
                if (f != null && f.FieldType == typeof(float)) { f.SetValue(b, value); return; }
            }
        }
    }
}
