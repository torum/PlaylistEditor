using PlaylistEditor.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace PlaylistEditor.Services;

public interface IConfigurationService
{
    void Initialize(string configFilePath);
    Task<Models.Configuration> LoadConfigurationAsync();
    Task SaveConfigurationAsync();
    Models.Configuration CurrentConfiguration { get; }
}