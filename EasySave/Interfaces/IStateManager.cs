namespace EasySave.Interfaces
{
    /// TODO
    /// Dev 1: call UpdateState() at each file transfer to keep state.json current.
    /// Dev 1: call ClearState() when a job finishes.

    //Interface for the StateManager
    public interface IStateManager
    {

        void UpdateState(Models.StateEntry entry);


        void ClearState(string jobName);
    }
}