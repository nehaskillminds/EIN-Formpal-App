// IFormDataMapper.cs
using EinAutomation.Api.Models;

namespace EinAutomation.Api.Services.Interfaces
{
    public interface IFormDataMapper
    {
        CaseData MapFormAutomationData(IDictionary<string, object> formData);
    }
}