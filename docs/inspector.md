# Inspector

## Funcionamento

O Hover Inspector é executado no Agent:

```text
cursor Win32 → UIA3 FromPoint → filtro por PID → mapeamento neutro
             → LocatorSet → overlay → snapshot IPC → painel WPF
```

O intervalo mínimo de 80 ms evita carga excessiva. O padrão é 120 ms no Agent e 160 ms para atualização visual do Desktop. Quando o elemento não muda, o overlay não é redesenhado.

## Highlight

O overlay usa uma Window WPF em thread STA própria e estilos Win32:

- always-on-top;
- `WS_EX_TRANSPARENT`;
- `WS_EX_NOACTIVATE`;
- `WS_EX_TOOLWINDOW`;
- borda azul e preenchimento translúcido.

Assim ele não recebe foco, não entra no Alt-Tab e não intercepta o clique.

## Propriedades coletadas

- Name, AutomationId, ClassName e ControlType;
- NativeWindowHandle;
- enabled, offscreen e focused;
- X, Y, largura e altura;
- parent identity;
- patterns suportados;
- LocatorSet ordenado por confiabilidade.

## Checklist Delphi

Para cada controle real, registrar:

1. componente e biblioteca (VCL, DevExpress, TMS, interno);
2. propriedades disponíveis em UIA3;
3. patterns disponíveis;
4. presença de HWND próprio;
5. resultado de click/foco/input;
6. diferença observada em UIA2, apenas se UIA3 falhar;
7. informação ausente antes de propor Win32/SendMessage/adapter.

Grids são apenas identificados nesta sprint. Leitura de linhas ou células só deve ser implementada após evidência do grid real.
