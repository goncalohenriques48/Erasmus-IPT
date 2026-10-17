using System.ComponentModel.DataAnnotations;

namespace appErasmus.Models {

    /*
     
    EnumTipoDocumento
    
        learning_Agreement
        Comprovativo_IBAN 
        Nota_Interna
        Transcicao_Notas
        Ficha_Selecao
        Carta_Aceitacao
        Reconhecimento_Academico
        Participante_Report
        Confirmacao_Estadia
     */

    /// <summary>
    /// Define as categorias possíveis de documentos (Ex: Comprovativo de IBAN, Learning Agreement).
    /// Associa cada tipo de documento ao departamento destinatário que o deve validar.
    /// </summary>
    public class CategoriaDocumento {
        /// <summary>
        /// Nome da categoria, que serve como chave primária (Ex: "Learning_Agreement").
        /// </summary>
        [Key]
        [Display(Name = "Nome da Categoria")]
        public string Nome { get; set; } = string.Empty;

        /// <summary>
        /// O papel (Role) do departamento ou grupo de utilizadores a quem este documento se destina.
        /// Controla quem tem acesso à visualização e validação destes ficheiros.
        /// </summary>
        [Required(ErrorMessage = "O departamento destinatário é de preenchimento obrigatório.")]
        [Display(Name = "Departamento Destinatário (Role)")]
        public string RoleDestinatario { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação para os documentos associados a esta categoria.
        /// </summary>
        public virtual ICollection<Documento> Documentos { get; set; } = [];
    }
}