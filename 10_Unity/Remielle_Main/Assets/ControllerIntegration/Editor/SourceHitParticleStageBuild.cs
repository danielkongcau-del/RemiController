namespace Remielle.Controller.Editor
{
 public static class SourceHitParticleStageBuild
 {
  public static void Run(){SourceHitParticleAudit.Run();SourceControllerBuild.BuildPlayer();}
 }
}
