using appErasmus.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace appErasmus.Data {
    public static class DbInitializer {
        public static void Initialize(ApplicationDbContext context) {
            context.Database.EnsureCreated();

            SeedCategoriasDocumentos(context);
            SeedCursosDoExcel(context);

            if (context.Universidades.Any()) {
                return;
            }

            var caminhoErasmus = Path.Combine(Directory.GetCurrentDirectory(), "Data", "SeedData", "erasmus.csv");
            if (!File.Exists(caminhoErasmus)) {
                throw new Exception($"ERRO CRÍTICO: Ficheiro erasmus.csv não encontrado!");
            }

            var caminhoInternacional = Path.Combine(Directory.GetCurrentDirectory(), "Data", "SeedData", "internacional.csv");
            if (!File.Exists(caminhoInternacional)) {
                throw new Exception($"ERRO CRÍTICO: Ficheiro internacional.csv não encontrado!");
            }

            // --- IMPORTAR ERASMUS ---
            // Colunas: Pais(0) ; Entidade(1) ; Erasmus Code(2) ; Areas(3) ; Morada(4) ; CodigoPostal(5) ; URL(6) ; Localidade(7)
            var linhasErasmus = File.ReadAllLines(caminhoErasmus).Skip(1);
            foreach (var linha in linhasErasmus) {
                if (string.IsNullOrWhiteSpace(linha)) continue;

                // Usamos apenas o ';' para evitar que vírgulas no meio das moradas partam a linha
                var colunas = linha.Split(';');

                if (colunas.Length >= 6 && !string.IsNullOrWhiteSpace(colunas[2])) {
                    var universidade = new Universidade {
                        ErasmusCode = colunas[2].Trim(),
                        Nome = colunas[1].Trim(),
                        Morada = colunas[4].Trim(),           // Agora usa a morada real!
                        Localidade = colunas[7].Trim(),             // A localidade real
                        CodigoPostal = colunas[5].Trim(),     // Agora usa o código postal real!
                        Url = colunas[6].Trim(),
                        Pais = colunas[0].Trim(),
                        TipoAcordo = "Acordos Bilaterais Erasmus+"
                    };

                    if (!context.Universidades.Any(u => u.ErasmusCode == universidade.ErasmusCode))
                        context.Universidades.Add(universidade);
                }
            }

            // --- IMPORTAR INTERNACIONAIS ---
            // Colunas: Pais(0) ; Entidade(1) ; Areas(2) ; Morada(3) ; CodigoPostal(4) ; URL(5) ; Localidade(6)
            var linhasInt = File.ReadAllLines(caminhoInternacional).Skip(1);
            int contadorID = 1;
            foreach (var linha in linhasInt) {
                if (string.IsNullOrWhiteSpace(linha)) continue;

                var colunas = linha.Split(';');

                if (colunas.Length >= 5 && !string.IsNullOrWhiteSpace(colunas[1])) {
                    // Prevenir erros de limite caso o nome do país tenha menos de 3 letras
                    string prefixoPais = colunas[0].Trim().ToUpper();
                    prefixoPais = prefixoPais.Substring(0, Math.Min(3, prefixoPais.Length));

                    var universidade = new Universidade {
                        ErasmusCode = $"INT-{prefixoPais}-{contadorID}",
                        Nome = colunas[1].Trim(),
                        Morada = colunas[3].Trim(),           // Morada real!
                        Localidade = colunas[6].Trim(),
                        CodigoPostal = colunas[4].Trim(),     // Código Postal real!
                        Url = colunas[5].Trim(),
                        Pais = colunas[0].Trim(),
                        TipoAcordo = "Parceiros PALOP, Brasil, China e Macau"
                    };

                    if (!context.Universidades.Any(u => u.ErasmusCode == universidade.ErasmusCode)) {
                        context.Universidades.Add(universidade);
                        contadorID++;
                    }
                }
            }

            context.SaveChanges();
        }

        public static async Task SeedRolesAndSuperUserAsync(IServiceProvider serviceProvider) {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<Utilizador>>();

            // Criacao das roles
            // REMOVIDO O "SAS"
            string[] nomesDasRoles = { "Aluno", "GRI", "CoordenadorErasmus", "Admin" };

            foreach (var roleName in nomesDasRoles) {
                var roleExist = await roleManager.RoleExistsAsync(roleName);
                if (!roleExist) {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Criacao dos utilizadores de teste
            var users = new[]{
                ("aluno20001@ipt.pt", "Aluno123!", "Aluno"),
                ("gri@ipt.pt", "Gri123!", "GRI"),
                ("coordenadorLEI@ipt.pt", "Coordenador123!", "CoordenadorErasmus"),
                ("admin@ipt.pt", "Admin123!", "Admin")
            };

            foreach (var (email, password, role) in users) {
                var user = await userManager.FindByEmailAsync(email);

                if (user == null)
                {
                    if (role == "Aluno")
                    {
                        string apenasNumeros = new String(email.Where(Char.IsDigit).ToArray());
                        int.TryParse(apenasNumeros, out int numeroAlunoExtraido);

                        user = new Aluno
                        {
                            NomeCompleto = "Aluno IPT",
                            NumeroAluno = numeroAlunoExtraido,
                            AnoCurricular = 1,
                            EmailConfirmed = true,
                            UserName = email,
                            Email = email,

                            // Obrigatorio para Aluno
                            NIF = "123456789",
                            TipoDocumento = EnumTipoDocumentoIdentificacao.CartaoCidadao,
                            NumeroDocumento = "12345678"
                        };

                    }
                    else
                    {
                        user = new Utilizador
                        {
                            UserName = email,
                            Email = email,
                            EmailConfirmed = true
                        };
                    }

                    var result = await userManager.CreateAsync(user, password);

                    if (!result.Succeeded)
                    {
                        var erros = string.Join(" | ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                        throw new Exception($"Erro ao criar utilizador {email}: {erros}");
                    }

                    if (!user.EmailConfirmed)
                    {
                        user.EmailConfirmed = true;
                        await userManager.UpdateAsync(user);
                    }

                }

                if (!await userManager.IsInRoleAsync(user, role))
                    await userManager.AddToRoleAsync(user, role);
            } 
        }

        private static void SeedCategoriasDocumentos(ApplicationDbContext context)
        {
            var categoriasBase = new[] {
                new CategoriaDocumento
                {
                    Nome = "Learning Agreement",
                    RoleDestinatario = "GRI"
                },
                new CategoriaDocumento
                {
                    Nome = "Comprovativo IBAN",
                    RoleDestinatario = "GRI"
                },
                new CategoriaDocumento
                {
                    Nome = "Transcrição de Notas",
                    RoleDestinatario = "CoordenadorErasmus"
                },
                new CategoriaDocumento
                {
                    Nome = "Ficha de Seleção",
                    RoleDestinatario = "CoordenadorErasmus"
                },
                new CategoriaDocumento
                {
                    Nome = "Carta de Aceitação",
                    RoleDestinatario = "GRI"
                },
                new CategoriaDocumento
                {
                    Nome = "Reconhecimento Académico",
                    RoleDestinatario = "CoordenadorErasmus"
                },
                new CategoriaDocumento
                {
                    Nome = "Participant Report",
                    RoleDestinatario = "GRI"
                },
                new CategoriaDocumento
                {
                    Nome = "Confirmação de Estadia",
                    RoleDestinatario = "GRI"
                },
                new CategoriaDocumento
                {
                    Nome = "Outro",
                    RoleDestinatario = "GRI"
                }
            };

            foreach (var categoria in categoriasBase)
            {
                var existe = context.CategoriasDocumentos
                    .Any(c => c.Nome == categoria.Nome);

                if (!existe)
                {
                    context.CategoriasDocumentos.Add(categoria);
                }
            }

            context.SaveChanges();
        }

        private static void SeedCursosDoExcel(ApplicationDbContext context) {
            // Se já existirem cursos, não volta a importar
            if (context.Cursos.Any()) {
                return;
            }

            var caminhoCursos = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Data",
                "SeedData",
                "Cursos-UCs-Normalizado.xlsx"
            );

            if (!File.Exists(caminhoCursos)) {
                throw new Exception("ERRO CRÍTICO: Ficheiro Cursos-UCs-Normalizado.xlsx não encontrado!");
            }

            using var workbook = new XLWorkbook(caminhoCursos);

            var folhaGraus = workbook.Worksheet("Graus");
            var folhaEscolas = workbook.Worksheet("Instituicoes");
            var folhaCursos = workbook.Worksheet("Cursos");
            var folhaDadosOriginais = workbook.Worksheet("DadosOriginais");

            // --- GRAUS / TIPOLOGIAS ---
            foreach (var linha in folhaGraus.RangeUsed()!.RowsUsed().Skip(1)) {
                var codigoGrau = linha.Cell(1).GetString().Trim();
                var nomeGrau = linha.Cell(2).GetString().Trim();

                if (string.IsNullOrWhiteSpace(codigoGrau) || string.IsNullOrWhiteSpace(nomeGrau))
                    continue;

                if (!context.GrausCurso.Any(g => g.CodigoGrau == codigoGrau)) {
                    context.GrausCurso.Add(new GrauCurso {
                        CodigoGrau = codigoGrau,
                        Nome = nomeGrau
                    });
                }
            }

            context.SaveChanges();

            // --- ESCOLAS ---
            foreach (var linha in folhaEscolas.RangeUsed()!.RowsUsed().Skip(1)) {
                var codigoEscola = linha.Cell(1).GetString().Trim();
                var nomeEscola = linha.Cell(2).GetString().Trim();

                if (string.IsNullOrWhiteSpace(codigoEscola) || string.IsNullOrWhiteSpace(nomeEscola))
                    continue;

                if (!context.Escolas.Any(e => e.CodigoEscola == codigoEscola)) {
                    context.Escolas.Add(new Escola {
                        CodigoEscola = codigoEscola,
                        Nome = nomeEscola
                    });
                }
            }

            context.SaveChanges();

            // --- CURSOS ---
            var graus = context.GrausCurso.ToList();
            var escolas = context.Escolas.ToList();

            var mapaCursoEscola = folhaDadosOriginais
                .RangeUsed()!
                .RowsUsed()
                .Skip(1)
                .GroupBy(r => r.Cell(1).GetString().Trim()) // CD_CURSO
                .ToDictionary(
                    g => g.Key,
                    g => g.First().Cell(6).GetString().Trim() // CD_INSTITUIC, usado como código da escola
                );

            foreach (var linha in folhaCursos.RangeUsed()!.RowsUsed().Skip(1)) {
                var codigoCurso = linha.Cell(1).GetString().Trim();
                var nomePT = linha.Cell(2).GetString().Trim();
                var nomeEN = linha.Cell(3).GetString().Trim();
                var codigoGrau = linha.Cell(4).GetString().Trim();

                if (string.IsNullOrWhiteSpace(codigoCurso))
                    continue;

                var grau = graus.FirstOrDefault(g => g.CodigoGrau == codigoGrau);

                if (grau == null)
                    continue;

                mapaCursoEscola.TryGetValue(codigoCurso, out var codigoEscola);

                if (string.IsNullOrWhiteSpace(codigoEscola))
                    continue;

                var escola = escolas.FirstOrDefault(e => e.CodigoEscola == codigoEscola);

                if (escola == null)
                    continue;

                if (!context.Cursos.Any(c => c.CodigoCurso == codigoCurso)) {
                    context.Cursos.Add(new Curso {
                        CodigoCurso = codigoCurso,
                        NomePTCurso = nomePT,
                        NomeENCurso = string.IsNullOrWhiteSpace(nomeEN) ? nomePT : nomeEN,
                        GrauCursoId = grau.Id,
                        EscolaId = escola.Id,
                        IsActive = true
                    });
                }
            }

            context.SaveChanges();
        }
    }
}