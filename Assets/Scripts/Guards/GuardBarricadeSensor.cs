using UnityEngine;

public class GuardBarricadeSensor : MonoBehaviour
{
    public GuardAI guardAI;

    void Awake()
    {
        if (guardAI == null) guardAI = GetComponentInParent<GuardAI>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (guardAI == null || guardAI.currentState == GuardState.Blocked) return;

        GameObject barricade = null;
        if (other.CompareTag("Barricade")) barricade = other.gameObject;
        else
        {
            var ctrl = other.GetComponentInParent<BarricadeController>();
            if (ctrl != null) barricade = ctrl.gameObject;
        }

        if (barricade != null) guardAI.TriggerBlocked(barricade);
    }
}
