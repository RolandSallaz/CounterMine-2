using System;
static class Program
{
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); Console.WriteLine("PASS "+message); }
    static void Tick(ConquestRules r,double until,int[] b,int[] o)
    { while(r.SimulatedAt < until-.00001) r.Step(Math.Min(until,r.SimulatedAt+.1),b,o); }
    static void Main() { try { Run(); } catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; } }
    static void Run()
    {
        int[] none={0,0,0}, blue={1,0,0}, orange={1,0,0};
        var r=new ConquestRules(1,100);
        Tick(r,109.9,blue,none);Check(r.Owner[0]==0,"No early capture");
        Tick(r,110,blue,none);Check(r.Owner[0]==1,"Ten-second capture");
        Tick(r,120,none,none);Check(Math.Abs(r.BlueScore-10)<.01,"One point per second while held");
        Tick(r,125,blue,orange);Check(r.Contested[0]&&r.Control[0]==1&&Math.Abs(r.BlueScore-10)<.01,"Contested capture and scoring freeze");
        Tick(r,135,none,orange);Check(r.Owner[0]==0,"Enemy neutralizes before taking ownership");
        Tick(r,145,none,orange);Check(r.Owner[0]==2,"Enemy captures after neutralization");
        var copy=ConquestRules.Read(r.Pack());Check(copy!=null&&copy.Round==1&&copy.Owner[0]==2&&copy.StartedAt==100,"Late join/master migration snapshot");
        r.Control[0]=0;Check(copy.Control[0]==-1,"Snapshots do not share mutable arrays");
        Tick(copy,700,none,none);double end=copy.OrangeScore;copy.Step(705,blue,none);
        Check(!copy.Playing(700)&&copy.OrangeScore==end&&copy.Control[0]==-1,"Exactly ten minutes; scoring and capture stop");
        Check(copy.Winner==2,"Winning team from held-point score");
        var fresh=new ConquestRules(2,715);Check(fresh.BlueScore==0&&fresh.OrangeScore==0&&fresh.Owner[0]==0&&fresh.Playing(715),"New round resets objectives and totals");
        var stalled=new ConquestRules(1,0);stalled.Step(300,blue,none);Check(stalled.Owner[0]==0,"Host stall cannot instantly capture");
        var invalid=fresh.Pack();invalid[3]=float.NaN;Check(ConquestRules.Read(invalid)==null,"Reject malformed snapshot");
        Check(new ConquestRules(1,0).Winner==0,"Draw supported");
    }
}
