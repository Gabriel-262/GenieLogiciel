using EasySave.Models;

namespace EasySave.Interfaces;

public interface IStateManager
{
    void UpdateState(StateEntry entry);
    void ClearState(string jobName);
}
