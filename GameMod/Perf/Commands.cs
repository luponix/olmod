namespace GameMod.Perf
{
    static class Commands
    {
        public static void Register()
        {
            RenderExperiments.ReadFlags();
            if (Core.GameMod.FindArg("-ship-mesh-combine"))
                ShipMeshCombine.ForceOn();

            uConsole.RegisterCommand("audio_budget", "Set how many copies of one 3D sound clip can play at once beyond the exempt distance (0 = no cap): audio_budget [n] [exempt metres]", AudioBudget.CmdAudioBudget);
            uConsole.RegisterCommand("perf_particle_trim", "1 = return a pooled effect to its pool when its last particle is dead, 0 = engine timer (system duration)", ParticleLifetime.CmdTrim);
            uConsole.RegisterCommand("perf_tb_light_off", "1 = switch off a remote ship's thunderbolt light when it has faded out, as the engine does for the local ship. 0 = engine default", RemoteThunderboltLight.CmdToggle);
            uConsole.RegisterCommand("perf_mount_filter_cache", "1 = ship effect flashes reuse the weapon mount MeshFilter lists, 0 = engine lookup every frame", ShipEffectMesh.CmdMountFilterCache);
            uConsole.RegisterCommand("perf_effect_mesh_view_skip", "1 = ships outside the camera view do not submit their flash and cloak effect meshes, 0 = engine default", ShipEffectMesh.CmdEffectMeshViewSkip);
            uConsole.RegisterCommand("perf_ship_gui_layout_off", "1 = skip the IMGUI layout pass of every ship's debug OnGUI, 0 = engine default", ShipGuiLayout.CmdShipGuiLayout);
            uConsole.RegisterCommand("perf_ship_mesh_combine", "1 = bake each remote ship hull into one mesh per material, 0 = stock renderers (overrides the COMBINE SHIP MESHES option)", ShipMeshCombine.CmdShipMeshCombine);
            uConsole.RegisterCommand("perf_physics_autosync_off", "1 = disable Physics.autoSyncTransforms (opt-in perf experiment), 0 = engine default", RenderExperiments.CmdPerfAutoSync);
            uConsole.RegisterCommand("perf_bloom_off", "1 = disable both bloom image effects entirely, 0 = engine default", RenderExperiments.CmdPerfBloom);
            uConsole.RegisterCommand("perf_projlight_dist", "Cull projectile lights beyond this many meters from the camera (0 = off)", RenderExperiments.CmdProjLightDist);
            uConsole.RegisterCommand("perf_projlight_max", "Keep only the N nearest projectile lights lit (0 = off)", RenderExperiments.CmdProjLightMax);
            uConsole.RegisterCommand("perf_lightflash_dist", "Skip explosion/muzzle light flashes farther than this from the camera (0 = off)", RenderExperiments.CmdLightFlashDist);
            uConsole.RegisterCommand("perf_ship_shadows_off", "1 = ship hulls stop casting realtime shadows (level shadows stay), 0 = engine default", RenderExperiments.CmdShipShadows);
            uConsole.RegisterCommand("perf_shiplight_dist", "Cull ships' always-on point lights beyond this many meters from the camera (0 = off)", RenderExperiments.CmdShipLightDist);
        }
    }
}
