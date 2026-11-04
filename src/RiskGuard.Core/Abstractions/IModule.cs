using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RiskGuard.Core.Abstractions;

/// <summary>
/// Contrato de um modulo do monolito. A API compoe os modulos sem conhecer seus tipos internos;
/// cada modulo registra o proprio DI e expoe seu assembly para o scan de handlers do MediatR.
/// </summary>
public interface IModule
{
    string Name { get; }

    Assembly Assembly { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
