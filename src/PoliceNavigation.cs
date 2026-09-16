using System;
using Il2CppScheduleOne.NPCs;
using UnityEngine;
using UnityEngine.AI;

namespace CoordinatedPolice;

internal static class PoliceNavigation
{
    private static NavMeshPath? path;

    public static void reset()
    {
        path = null;
    }

    public static bool try_destination(NPCMovement movement, Vector3 candidate, out Vector3 destination)
    {
        destination = default;
        NavMeshAgent agent = movement.Agent;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.isOnOffMeshLink ||
            !float.IsFinite(candidate.sqrMagnitude)) return false;
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 0.75f, agent.areaMask)) return false;
        if (Math.Abs(hit.position.y - candidate.y) > 1f) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.FindClosestEdge(hit.position, out NavMeshHit edge, filter) || edge.distance < 0.25f) return false;
        path ??= new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        destination = hit.position;
        return true;
    }

    public static bool try_redirect(NPCMovement movement, Vector3 original, Vector3 candidate, out Vector3 destination)
    {
        destination = default;
        NavMeshAgent agent = movement.Agent;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || !float.IsFinite(original.sqrMagnitude)) return false;
        if (!NavMesh.SamplePosition(original, out NavMeshHit origin, 0.75f, agent.areaMask)) return false;
        if (!try_destination(movement, candidate, out destination)) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        return !NavMesh.Raycast(origin.position, destination, out _, filter);
    }
}
