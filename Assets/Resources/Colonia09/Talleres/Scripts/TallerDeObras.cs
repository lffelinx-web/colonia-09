using UnityEngine;

namespace Colonia09
{
    [DisallowMultipleComponent]
    public sealed class TallerDeObras : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Aumento porcentual de la velocidad de construcción de obras y conexiones.")]
        private int bonificacionVelocidadPorcentaje;

        public int BonificacionVelocidadPorcentaje => bonificacionVelocidadPorcentaje;
        public float MultiplicadorVelocidad => 1f + bonificacionVelocidadPorcentaje / 100f;
    }
}

