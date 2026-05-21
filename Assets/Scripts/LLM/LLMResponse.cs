using System;

[Serializable]
public class LLMResponse
{
    public DoorCommand[] lock_doors;
    public DoorCommand[] unlock_doors;
    public GuardCommand[] guard_targets;
    public int[] exit_room;
    public string reasoning;
}

[Serializable]
public class DoorCommand
{
    public int[] room;
    public int door;
}

[Serializable]
public class GuardCommand
{
    public int guard_id;
    public int[] target_room;
}
