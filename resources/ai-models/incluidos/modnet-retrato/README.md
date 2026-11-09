# MODNet (recorte de pessoas)

Modelo incluído no Meu Editor de Vídeo: o recurso **Remover fundo com IA** funciona sem instalar pacotes.

- **Uso:** aba **IA** das propriedades do clipe › *Remover fundo com IA*. Ajuste o efeito "Remover fundo (IA)" na aba
  Efeitos: fundo (transparente, cor, desfoque, imagem ou vídeo), manter a pessoa ou o fundo, limiar, contrair/expandir,
  suavizar borda e **suavização entre quadros** (reduz a oscilação da borda de um quadro para o outro).
- **Rastrear pessoa** e **Calcular máscaras** também usam este modelo.
- **Entrada:** imagem RGB com o lado menor redimensionado para 512 px (múltiplos de 32). **Saída:** máscara alfa 0…1.
- Roda na GPU (DirectML) ou na CPU, conforme Ferramentas › Modelos de IA.
- Tudo é processado no computador; nada é enviado para a internet.

Origem: ZHKKKe/MODNet (Apache-2.0), exportação ONNX de huggingface.co/Xenova/modnet.
