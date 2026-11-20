using UnityEngine;

namespace Colonia09
{
    [DisallowMultipleComponent]
    public sealed class GranjaHidroponica : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Unidades de comida que produce este nivel en una hora de juego.")]
        private int comidaPorHora;

        public int ComidaPorHora => comidaPorHora;
    }
}

