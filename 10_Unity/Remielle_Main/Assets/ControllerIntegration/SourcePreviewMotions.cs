namespace Remielle.Controller
{
    public static class SourcePreviewMotions
    {
        // Names select a state within this exact controller; its motion slot
        // still resolves through block/CAB/pathID. No name-based clip lookup.
        public static void Prewarm(SourceActionSession session)=>session.Prewarm(
            "Idle","Walk_Start","Walk_Loop","Walk_End","Walk_Start_End",
            "Walk_To_RunLoop_01","Run_End","RunLoop_01","RunLoop_02","RunLoop_01_To_02","RunLoop_02_To_01",
            "Evade_Front","Evade_Back","Evade_Front_02","Evade_Back_02","Evade_Front_03","Evade_To_RunLoop_01","Evade_To_RunLoop_02",
            "Dash_Start","Dash_Loop","Dash_End","Dash_Evade","Dash_Loop_02",
            "Attack_Rush","Attack_Rush_End","Attack_Normal_01","Attack_Normal_02","Attack_Normal_03","Attack_Normal_04",
            "Attack_Normal_01_End","Attack_Normal_02_End","Attack_Normal_03_End","Attack_Normal_04_End",
            "Attack_Burst_01","Attack_Burst_01_End",
            "Attack_Special","Attack_Special_End","Attack_ExSpecial","Attack_ExSpecial_End");
    }
}
