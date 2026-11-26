namespace CadDustLab;

// Exactly one calculation, one replaceable pending request and one completed result.
// This class and its worker only handle managed values; they never call AutoCAD.
public sealed class LatestWorker<TRequest,TResult>:IDisposable where TRequest:class where TResult:class
{
    private readonly object gate=new();
    private readonly AutoResetEvent signal=new(false);
    private readonly Func<TRequest,TResult> calculate;
    private TRequest? pending;
    private TResult? ready;
    private Exception? error;
    private bool stopped;
    private long coalesced,completed;
    public long Coalesced=>Interlocked.Read(ref coalesced);
    public long Completed=>Interlocked.Read(ref completed);
    public LatestWorker(Func<TRequest,TResult> calculate)
    {
        this.calculate=calculate;
        new Thread(Run){IsBackground=true,Name="Dust frame preparation",Priority=ThreadPriority.BelowNormal}.Start();
    }
    public void Submit(TRequest request)
    {
        lock(gate){if(stopped)return;if(pending is not null)Interlocked.Increment(ref coalesced);pending=request;signal.Set();}
    }
    public TResult? Take()=>Interlocked.Exchange(ref ready,null);
    public Exception? TakeError()=>Interlocked.Exchange(ref error,null);
    private void Run()
    {
        try
        {
            while(true)
            {
                signal.WaitOne();
                while(true)
                {
                    TRequest? request;
                    lock(gate){if(stopped)return;request=pending;pending=null;}
                    if(request is null)break;
                    TResult result;
                    try{result=calculate(request);}catch(Exception ex){Interlocked.Exchange(ref error,ex);continue;}
                    lock(gate)
                    {
                        if(stopped)return;
                        Interlocked.Exchange(ref ready,result);Interlocked.Increment(ref completed);
                    }
                }
            }
        }
        finally{signal.Dispose();}
    }
    public void Dispose()
    {
        lock(gate){if(stopped)return;stopped=true;pending=null;Interlocked.Exchange(ref ready,null);signal.Set();}
        // Never join a calculation from the UI thread. It finishes and cleans up itself.
    }
}
