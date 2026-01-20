namespace ServerCs.Actor;

public class Worker
{
    public int Id { get; set; }
    public int EachNum { get; set; }

    public Worker(int id, int eachNum)
    {
        Id = id;
        EachNum = eachNum;
    }

    public void Run()
    {
        while (Starnet.Instance.IsRunning)
        {
            var srv = Starnet.Instance.PopGlobalQueue();
            if (srv == null)
            {
                Starnet.Instance.WorkerWait();
            }
            else
            {
                srv.ProcessMsgs(EachNum);
                CheckAndPutGlobal(srv);
            }
        }
    }

    private void CheckAndPutGlobal(Service srv)
    {
        if (srv.IsExiting)
        {
            return;
        }

        if (srv.HasMessages)
        {
            if (!srv.InGlobal)
            {
                Starnet.Instance.PushGlobalQueue(srv);
                srv.SetInGlobal(true);
            }
        }
        else
        {
            srv.SetInGlobal(false);
        }
    }
}
