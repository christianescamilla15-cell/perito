using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using Perito.Application.Abstracciones;
using Perito.Asistente;
using Perito.Domain.Comun;
using Perito.Domain.Privacidad;
using Perito.EnMemoria;
using Perito.Evaluacion;
using Perito.Infrastructure.Reloj;

namespace Perito.Architecture.Tests;

/// <summary>Las capas y las garantías del asistente se hacen cumplir con pruebas sobre los tipos y el IL, no con un
/// diagrama.</summary>
public class ArquitecturaTests
{
    private static readonly Assembly Dominio = typeof(IClock).Assembly;
    private static readonly Assembly Aplicacion = typeof(IMediador).Assembly;
    private static readonly Assembly Infraestructura = typeof(SystemClock).Assembly;
    private static readonly Assembly Asistente = typeof(AsistenteDeTriaje).Assembly;
    private static readonly Assembly Api = typeof(Perito.Api.Endpoints).Assembly;
    private static readonly Assembly EnMemoria = typeof(AlmacenEnMemoria).Assembly;
    private static readonly Assembly Evaluacion = typeof(Evaluador).Assembly;
    private static readonly Assembly[] Todos = [Dominio, Aplicacion, Infraestructura, Asistente, Api, EnMemoria, Evaluacion];

    private static void SinDependencias(Assembly ensamblado, params string[] prohibidas)
    {
        var r = Types.InAssembly(ensamblado).ShouldNot().HaveDependencyOnAny(prohibidas).GetResult();
        Assert.True(r.IsSuccessful, string.Join(", ", r.FailingTypeNames ?? []));
    }

    /// <summary>Si alguien corre estas pruebas con cobertura, coverlet instrumenta el IL y las demás fallarían por una
    /// razón que no es de arquitectura.</summary>
    [Fact]
    public void Los_ensamblados_que_se_inspeccionan_no_estan_instrumentados() =>
        Assert.DoesNotContain(Todos, a => a.GetTypes().Any(t => t.FullName?.StartsWith("Coverlet.", StringComparison.Ordinal) == true));

    [Fact]
    public void El_dominio_no_depende_de_ningun_otro_proyecto_ni_paquete() =>
        SinDependencias(Dominio, "Perito.Application", "Perito.Infrastructure", "Perito.Asistente", "Perito.Api",
            "Microsoft", "Npgsql", "OllamaSharp");

    [Fact]
    public void El_dominio_solo_referencia_la_biblioteca_base() =>
        Assert.All(Dominio.GetReferencedAssemblies(), r => Assert.StartsWith("System", r.Name!, StringComparison.Ordinal));

    [Fact]
    public void El_dominio_no_hace_entrada_ni_salida() =>
        SinDependencias(Dominio, "System.IO", "System.Net", "System.Data", "System.Net.Http");

    [Fact]
    public void La_aplicacion_no_conoce_la_infraestructura_el_asistente_ni_los_paquetes() =>
        SinDependencias(Aplicacion, "Perito.Infrastructure", "Perito.Asistente", "Perito.Api",
            "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.SemanticKernel", "OllamaSharp");

    [Fact]
    public void La_infraestructura_no_conoce_al_asistente_ni_a_quien_la_compone() =>
        SinDependencias(Infraestructura, "Perito.Asistente", "Perito.Api", "Microsoft.SemanticKernel");

    [Fact]
    public void El_asistente_no_conoce_la_base_ni_la_infraestructura() =>
        SinDependencias(Asistente, "Perito.Infrastructure", "Perito.Api", "Microsoft.EntityFrameworkCore", "Npgsql");

    [Fact]
    public void Ni_las_filas_ni_EF_salen_de_la_infraestructura() =>
        SinDependencias(Api, "Perito.Infrastructure.Persistencia", "Microsoft.EntityFrameworkCore", "Npgsql");

    /// <summary>
    /// GEN-12, en el IL: el asistente sólo construye CONSULTAS (nada que implemente IComando), no usa la unidad de
    /// trabajo ni los métodos Agregar de los repositorios, y de los agregados del dominio sólo lee propiedades.
    /// </summary>
    [Fact]
    public void El_asistente_no_envia_comandos_ni_cambia_agregados()
    {
        var hallazgos = new List<string>();
        var consultasCreadas = 0;
        foreach (var (tipo, metodo, llamada) in Llamadas(Asistente))
        {
            var declarante = llamada.DeclaringType.Resolve();
            if (declarante is null || !declarante.Module.Assembly.Name.Name.StartsWith("Perito.", StringComparison.Ordinal) ||
                declarante.Module.Assembly.Name.Name == Asistente.GetName().Name)
            {
                continue;
            }

            var nombre = $"{tipo}.{metodo} → {declarante.FullName}::{llamada.Name}";
            if (llamada.Name == ".ctor" && Implementa(declarante, "IComando`1"))
            {
                hallazgos.Add("crea un comando: " + nombre);
            }
            else if (llamada.Name == ".ctor" && Implementa(declarante, "IConsulta`1"))
            {
                consultasCreadas++;
            }
            else if (declarante.FullName is "Perito.Application.Puertos.IUnidadDeTrabajo" || llamada.Name == "Agregar")
            {
                hallazgos.Add("escribe: " + nombre);
            }
            else if (EsAgregado(declarante) && !llamada.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                hallazgos.Add("cambia un agregado: " + nombre);
            }
        }

        Assert.Empty(hallazgos);
        Assert.True(consultasCreadas > 0, "la prueba debe ver la consulta de la función de la póliza");
    }

    /// <summary>Las únicas funciones que ve el modelo son las de la lista blanca, y no reciben parámetros.</summary>
    [Fact]
    public void Las_unicas_funciones_del_kernel_son_las_de_la_lista_blanca()
    {
        var funciones = Asistente.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "KernelFunctionAttribute"))
            .ToList();
        var funcion = Assert.Single(funciones);
        Assert.Equal(typeof(FuncionesDePoliza), funcion.DeclaringType);
        Assert.All(funcion.GetParameters(), p => Assert.Equal(typeof(CancellationToken), p.ParameterType));
        Assert.Single(AsistenteDeTriaje.Permitidas);
    }

    /// <summary>Sólo el seudonimizador crea texto para el asistente: el tipo no tiene constructor público.</summary>
    [Fact]
    public void Solo_el_seudonimizador_crea_el_texto_que_recibe_el_asistente()
    {
        Assert.Empty(typeof(TextoSeudonimizado).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        var creadores = Todos.SelectMany(Llamadas)
            .Where(x => x.Llamada.Name == ".ctor" && x.Llamada.DeclaringType.FullName == typeof(TextoSeudonimizado).FullName)
            .Select(x => x.Tipo).Distinct().ToList();
        Assert.Equal(["Perito.Domain.Privacidad.Seudonimizador"], creadores);
    }

    /// <summary>Los eventos de dominio llevan ids, fechas, enumeraciones, montos y a lo más una cláusula: ningún campo de
    /// texto libre donde pudiera viajar un dato personal.</summary>
    [Fact]
    public void Los_eventos_de_dominio_no_tienen_campos_de_texto_libre()
    {
        var eventos = Dominio.GetTypes().Where(t => t.IsClass && typeof(IEventoDeDominio).IsAssignableFrom(t)).ToList();
        Assert.True(eventos.Count >= 9);
        var texto = eventos.SelectMany(e => e.GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => $"{e.Name}.{p.Name}")).ToList();
        Assert.Equal(["SiniestroRechazado.Clausula"], texto);
    }

    /// <summary>Nadie lee el reloj del sistema salvo SystemClock: se recorre el IL de todos los métodos de los siete
    /// ensamblados buscando las propiedades del reloj.</summary>
    [Fact]
    public void Nadie_usa_el_reloj_del_sistema_salvo_SystemClock()
    {
        string[] prohibidos =
        [
            "System.DateTime::get_Now", "System.DateTime::get_UtcNow", "System.DateTime::get_Today",
            "System.DateTimeOffset::get_Now", "System.DateTimeOffset::get_UtcNow", "System.TimeProvider::get_System",
        ];
        var usos = Todos.SelectMany(Llamadas)
            .Where(x => prohibidos.Contains($"{x.Llamada.DeclaringType.FullName}::{x.Llamada.Name}") ||
                        x.Llamada.FullName == "System.Guid System.Guid::CreateVersion7()")
            .Select(x => x.Tipo).ToList();
        Assert.Equal([typeof(SystemClock).FullName!], usos);
    }

    private static bool EsAgregado(TypeDefinition tipo)
    {
        for (var t = tipo; t is not null; t = t.BaseType?.Resolve())
        {
            if (t.FullName == typeof(RaizDeAgregado).FullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Implementa(TypeDefinition tipo, string interfaz) =>
        tipo.Interfaces.Any(i => i.InterfaceType.Name == interfaz || (i.InterfaceType.Resolve() is { } d && Implementa(d, interfaz)));

    private static IEnumerable<(string Tipo, string Metodo, MethodReference Llamada)> Llamadas(Assembly ensamblado)
    {
        using var modulo = ModuleDefinition.ReadModule(ensamblado.Location, new ReaderParameters
        {
            AssemblyResolver = Resolutor(),
        });
        var lista = new List<(string, string, MethodReference)>();
        foreach (var tipo in modulo.GetTypes())
        {
            foreach (var metodo in tipo.Methods.Where(m => m.HasBody))
            {
                lista.AddRange(metodo.Body.Instructions
                    .Where(i => i.OpCode.FlowControl == FlowControl.Call || i.OpCode.Code == Code.Newobj)
                    .Select(i => i.Operand).OfType<MethodReference>()
                    .Select(llamada => (NombreExterno(tipo), metodo.Name, llamada)));
            }
        }

        return lista;
    }

    /// <summary>Los métodos async y lambdas viven en tipos anidados generados: se reportan por su tipo contenedor.</summary>
    private static string NombreExterno(TypeDefinition tipo)
    {
        while (tipo.DeclaringType is not null && tipo.Name.Contains('<', StringComparison.Ordinal))
        {
            tipo = tipo.DeclaringType;
        }

        return tipo.FullName.Replace('/', '+');
    }

    private static DefaultAssemblyResolver Resolutor()
    {
        var r = new DefaultAssemblyResolver();
        r.AddSearchDirectory(AppContext.BaseDirectory);
        r.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        return r;
    }
}
