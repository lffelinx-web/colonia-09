using UnityEngine;

namespace Colonia09
{
    [DisallowMultipleComponent]
    public sealed class Laboratorio : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Datos de investigación que produce este nivel en una hora de juego.")]
        private int datosPorHora;

        public int DatosPorHora => datosPorHora;
    }
}

