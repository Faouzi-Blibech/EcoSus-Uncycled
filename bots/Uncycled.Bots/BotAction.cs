namespace Uncycled.Bots
{
    /// <summary>
    /// The discrete choices a bot makes every decision. Stable order: never reorder or remove once a
    /// model is trained; append new ones at the end (the schema hash covers the names and order).
    /// Zones are in map order; slots are the visible players nearest first.
    /// The first ActionCatalogue.Count members are the ones the networks were trained on (the v1 set); the v2 members after them are played by the scripted brains only.
    /// </summary>
    public enum BotAction
    {
        Stay,
        GoNextTask,
        GoTask2,
        GoZone0,
        GoZone1,
        GoZone2,
        GoEmergency,
        GoFireA,
        GoFireB,
        GoSmogPanel,
        FollowSlot0,
        FollowSlot1,
        FollowSlot2,
        FollowSlot3,
        AvoidSlot0,
        AvoidSlot1,
        AvoidSlot2,
        AvoidSlot3,
        UseHere,
        ReportNearest,
        CallMeeting,
        CleanNearest,
        HoldFire,
        ReleaseFire,
        RepairSmog,
        HitNearest,
        DumpHere,
        SabotageFire,
        SabotageSmog,
        FakeTaskHere,

        // v2 (appended: the trained networks know the 30 above and nothing else)
        GoDirtySpot,
        FinishSpot,
        GoMachine,
        Deliver,
        PickUpgrade,
        Repollute,
        PickAbility,
        PlantVirus,
        GasRoom,
        HealSelf,
        FixMachine,
        SabotageMachine,

        /// <summary>Walk out of a gas room whose warning runs (the public gas warning; a Cleaner without a mask).</summary>
        LeaveGas,
    }
}
