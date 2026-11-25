using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
using Perito.Application.Abstracciones;
using Perito.Application.Puertos;
using Perito.Asistente.Recuperacion;

namespace Perito.Asistente.Proveedores;

/// <summary>
/// Proveedores intercambiables del servicio de chat. Probados: «extractivo» (pruebas y CI) y «ollama» (local, modelo
/// chico). Ningún otro proveedor está incluido ni probado; se conectaría registrando su IChatCompletionService.
/// </summary>
public static class Fabrica
{
    /// <summary>Crea el servicio de chat del proveedor configurado.</summary>
    public static IChatCompletionService Chat(OpcionesDelAsistente opciones) => opciones.Proveedor switch
    {
        "extractivo" => new ModeloExtractivo(),
        // El conector de Ollama de SK (alfa) recomienda el cliente de OllamaSharp como IChatClient adaptado a SK.
        "ollama" => new OllamaApiClient(new Uri(opciones.Endpoint), opciones.Modelo).AsChatCompletionService(),
        _ => throw new InvalidOperationException(
            $"Proveedor «{opciones.Proveedor}» no soportado: use «extractivo» u «ollama».")
    };

    /// <summary>Registra el asistente: recuperador (índice una vez), servicio de chat y el asistente por alcance.</summary>
    public static IServiceCollection AgregarAsistente(this IServiceCollection servicios, OpcionesDelAsistente opciones)
    {
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IRecuperadorDeClausulas>(new RecuperadorBm25());
        servicios.AddSingleton(Chat(opciones));
        servicios.AddScoped<IAsistenteDeTriaje>(p => new AsistenteDeTriaje(p.GetRequiredService<IChatCompletionService>(),
            p.GetRequiredService<IRecuperadorDeClausulas>(), p.GetRequiredService<IMediador>(), opciones));
        return servicios;
    }
}

/// <summary>Ajustes de ejecución por proveedor.</summary>
public static class Ejecucion
{
    /// <summary>Temperatura 0 y llamada a funciones sin invocación automática: el asistente decide qué se ejecuta.</summary>
    public static PromptExecutionSettings Ajustes(OpcionesDelAsistente opciones)
    {
        // La temperatura va como dato de extensión: el adaptador de IChatClient la traduce a ChatOptions.
        return new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
            ExtensionData = opciones.Proveedor == "extractivo"
                ? null
                : new Dictionary<string, object> { ["temperature"] = 0.0 },
        };
    }

    /// <summary>Nombre del modelo que queda en la propuesta.</summary>
    public static string NombreDelModelo(OpcionesDelAsistente opciones) =>
        opciones.Proveedor == "extractivo" ? ModeloExtractivo.Nombre : opciones.Modelo;
}
