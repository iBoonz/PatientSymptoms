using ProactiveBot.Models;
using System.Threading.Tasks;

namespace ProactiveBot.Services
{
    public interface IFHIRService
    {
        public Task SendDataToFHIRServer(PatientSymptomInfoDto patientSymptoms);
    }
}
