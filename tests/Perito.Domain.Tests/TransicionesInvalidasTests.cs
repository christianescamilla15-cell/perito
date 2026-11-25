using Perito.Domain.Ajustadores;
using Perito.Domain.Comun;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using static Perito.Domain.Tests.Escenario;

namespace Perito.Domain.Tests;

/// <summary>
/// La máquina de estados completa: cada acción en cada estado donde no está permitida falla con
/// «transicion_invalida» y no cambia el estado. Las permitidas se prueban en su propio archivo.
/// </summary>
public class TransicionesInvalidasTests
{
    private static readonly Dictionary<EstadoSiniestro, string[]> Permitidas = new()
    {
        [EstadoSiniestro.Avisado] = ["triar"],
        [EstadoSiniestro.Triado] = ["asignar"],
        [EstadoSiniestro.Asignado] = ["registrar_dato", "reservar", "dictaminar"],
        [EstadoSiniestro.Reservado] = ["registrar_dato", "ajustar_reserva", "dictaminar"],
        [EstadoSiniestro.Dictaminado] = ["pagar"],      // el dictamen de este escenario es procedente
        [EstadoSiniestro.Pagado] = [],
        [EstadoSiniestro.Rechazado] = [],
    };

    private static readonly string[] Acciones =
        ["triar", "asignar", "registrar_dato", "reservar", "ajustar_reserva", "dictaminar", "pagar", "rechazar"];

    public static TheoryData<EstadoSiniestro, string> NoPermitidas()
    {
        var datos = new TheoryData<EstadoSiniestro, string>();
        foreach (var (estado, permitidas) in Permitidas)
        {
            foreach (var accion in Acciones.Except(permitidas))
            {
                datos.Add(estado, accion);
            }
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(NoPermitidas))]
    public void Una_accion_fuera_de_su_estado_falla_y_no_cambia_nada(EstadoSiniestro estado, string accion)
    {
        var poliza = AutoAmplia();
        var s = EnEstado(estado, poliza);
        var eventosAntes = s.Eventos.Count;

        Resultado r = accion switch
        {
            "triar" => s.Triar(TipoDeSiniestro.Colision, poliza, 0, "ajustador.uno", null, Ahora),
            "asignar" => s.AsignarAjustador(AjustadorDe(Especialidad.Auto), 0, Ahora),
            "registrar_dato" => s.RegistrarDatoRecibido(DatoRequerido.Lugar),
            "reservar" => s.ConstituirReserva(poliza, 50_000m, Ahora),
            "ajustar_reserva" => s.AjustarReserva(poliza, 60_000m, "peritaje", Ahora),
            "dictaminar" => s.Dictaminar(poliza, new EntradaDeDictamen(50_000m, [], null), "ajustador.dos", Hoy, Ahora),
            "pagar" => s.Pagar("SPEI-9", "tesoreria.uno", Hoy, Ahora),
            _ => s.Rechazar(Hoy, Ahora),
        };

        Falla(r, "transicion_invalida");
        Assert.Equal(estado, s.Estado);
        Assert.Equal(eventosAntes, s.Eventos.Count);
    }

    [Fact]
    public void La_matriz_cubre_las_cincuenta_y_seis_combinaciones() =>
        Assert.Equal(Enum.GetValues<EstadoSiniestro>().Length * Acciones.Length,
            NoPermitidas().Count + Permitidas.Values.Sum(p => p.Length));

    private static Siniestro EnEstado(EstadoSiniestro estado, Polizas.Poliza poliza)
    {
        switch (estado)
        {
            case EstadoSiniestro.Avisado:
                return Avisado(poliza);
            case EstadoSiniestro.Triado:
                return Triado(poliza);
            case EstadoSiniestro.Asignado:
                return Asignado(poliza);
            case EstadoSiniestro.Reservado:
                return Reservado(poliza);
            case EstadoSiniestro.Dictaminado:
                return Dictaminado(poliza);
            case EstadoSiniestro.Pagado:
                var pagado = Dictaminado(poliza);
                Exito(pagado.Pagar("SPEI-1", "tesoreria.uno", Hoy, Ahora));
                return pagado;
            default:
                var rechazado = Reservado(poliza);
                Exito(rechazado.Dictaminar(poliza, new EntradaDeDictamen(1_000m, [], null), "ajustador.uno", Hoy,
                    Ahora));
                Exito(rechazado.Rechazar(Hoy, Ahora));
                return rechazado;
        }
    }
}
