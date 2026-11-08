# Meu Editor de Vídeo

Editor de vídeo desktop para **Windows 11 (64 bits)**, em **Português do Brasil**, com processamento 100% local
(inclusive IA). Licenciado sob **GPL-3.0-or-later**.

- Especificação: [`ESPECIFICACAO_TECNICA_E_FUNCIONAL.md`](ESPECIFICACAO_TECNICA_E_FUNCIONAL.md)
- Plano de implantação: [`documentation/PLANO_DE_IMPLANTACAO.md`](documentation/PLANO_DE_IMPLANTACAO.md)
- Dependências e licenças: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md)

## Requisitos de desenvolvimento

- Windows 11 x64
- .NET SDK 10.0.4xx
- PowerShell 7 (`pwsh`)

## Compilar e testar

```powershell
pwsh scripts/fetch-deps.ps1        # baixa e verifica FFmpeg (necessário a partir da F2)
dotnet build MeuEditorDeVideo.sln
dotnet test MeuEditorDeVideo.sln
dotnet run --project src/MeuEditorDeVideo.App
```

## Estrutura

```
src/        App (WPF/MVVM), Application, Domain, Core, Infrastructure e motores
            (Project, Media, Timeline, Image, Audio, AI, Rendering)
tests/      UnitTests, IntegrationTests, MediaTests
resources/  modelos de exportação, transições, efeitos, LUTs, fontes
scripts/    obtenção/verificação de dependências
installer/  instalador MSI (F12)
documentation/
```

## Privacidade

O aplicativo não faz chamadas de rede. Isso é verificado por testes automatizados
(`tests/MeuEditorDeVideo.UnitTests/Architecture/PrivacyTests.cs`).
