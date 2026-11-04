using MediatR;
using RiskGuard.Modules.Tenant.Application.Commands;

namespace RiskGuard.Api.Provisioning;

/// <summary>
/// Cadastro de lojista por linha de comando (operacao do dono da plataforma; nao ha rota publica de cadastro):
/// <c>dotnet RiskGuard.Api.dll --provision-tenant "Nome da Loja" dono@loja.com</c>. Cria o lojista e o usuario dono no provedor de
/// identidade e imprime o link para o dono definir a senha. O link e uma credencial de uso unico: sai somente no terminal.
/// </summary>
internal static class ProvisionTenantCli
{
    public const string Flag = "--provision-tenant";

    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        var index = args.ToList().IndexOf(Flag);
        if (index < 0 || args.Count < index + 3)
        {
            await Console.Error.WriteLineAsync($"Uso: {Flag} \"<nome do lojista>\" <email do dono>");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RegisterTenantCommand(args[index + 1], args[index + 2]));

        if (!result.IsSuccess)
        {
            await Console.Error.WriteLineAsync($"Falha ao cadastrar: [{result.Error.Code}] {result.Error.Message}");
            return 1;
        }

        var tenant = result.Value;
        Console.WriteLine($"Lojista criado: {tenant.TenantId}");
        Console.WriteLine($"Usuario no provedor: {tenant.ExternalUserId}");
        Console.WriteLine(tenant.PasswordSetupUrl is null
            ? "O provedor nao gerou o link de senha: envie 'redefinir senha' pelo painel do provedor."
            : $"Link para o dono definir a senha (uso unico, nao compartilhe em canais abertos): {tenant.PasswordSetupUrl}");
        return 0;
    }
}
