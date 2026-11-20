using UnityEngine;

namespace Colonia09
{
    [DisallowMultipleComponent]
    public sealed class TorreDeAgua : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Unidades de agua que suministra este nivel en una hora de juego.")]
        private int aguaPorHora;

        public int AguaPorHora => aguaPorHora;
    }
}

