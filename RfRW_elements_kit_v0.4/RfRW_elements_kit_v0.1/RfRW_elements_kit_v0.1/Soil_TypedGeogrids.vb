Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports System.Math
Imports Shape = Autodesk.Civil.DatabaseServices.Shape


Public Class Soil_TypedGeogrids
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: BasicLane
    '
    '   Description: Creates a simple cross-sectional representation of a reinforcement soil wall composed of an array of geogrids.Attachment origin
    '                is at bottom.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetSurface              Surface    Yes       May be used to judge fill/cut condition
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                FaceAngle              double      no          0                degrees of face element slope
    '                Side                   long        no          Right            specifies side to place SA on
    '                Width                  double      no          3.0              width of geogrids
    '                Step                   double      no          0.5              step of geogrid layer
    '                GravelSlope            double      no          1.5              0
    '                DrenageOffset          double      no          0.3              0
    '                DrenageElevation       long        no           1               0
    '                GeotextileOverlap      double      no          0.3              0
    '                BaseElevation          double      no          0.15             0
    '                RE520_count            long        no          0                0
    '                RE540_count            long        no          0                0
    '                RE560_count            long        no          0                0
    '                RE570_count            long        no          0                0
    '                RE580_count            long        no          0                0
    '                RE520_length           double      no          3                0
    '                RE540_length           double      no          3                0
    '                RE560_length           double      no          3                0
    '                RE570_length           double      no          3                0
    '                RE580_length           double      no          3                0
    '
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None
    Private Const dFaceAngleDefault = 4.0
    Private Const dHorizOffset = 0.0
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const dGridWidthDefault = 3.0
    Private Const dLayerStepDefault = 0.45
    Private Const dGravelSlopeDefault = 1.0
    Private Const dDrenageOffsetDefault = 0.3
    'Private Const dDrenageElevationDefault = 1
    Private Const dGeotextileOverlapDefault = 1.0
    Private Const dBaseElevationDefault = 0.15
    Private Const dRE520_countDefault = 0
    Private Const dRE540_countDefault = 0
    Private Const dRE560_countDefault = 0
    Private Const dRE570_countDefault = 0
    Private Const dRE580_countDefault = 0
    'Private Const dSubAsNameDefault = "Участок"
    Private Const blocksAboveGrid As Integer = 0
    Private Const dBlocksCount As Integer = 1
    Private Const dBlockH = 0.15

    'Private Const dRE520_lengthDefault = 3
    'Private Const dRE540_lengthDefault = 3
    'Private Const dRE560_lengthDefault = 3
    'Private Const dRE570_lengthDefault = 3
    'Private Const dRE580_lengthDefault = 3


    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        'retrieve paramater buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("Проектный профиль", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"

        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"

    End Sub
    Protected Overrides Sub GetInputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)

        ' define collection for long parameters in corridor
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        ' define collection for double parameters in corridor
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        ' define collection for string parameters in corridor
        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        ' Add input parameters we used in this script
        paramsDouble.Add("Наклон лицевой грани", dFaceAngleDefault)
        paramsDouble.Add("Горизонтальное смещение", dHorizOffset)
        paramsDouble.Add("Длина георешеток", dGridWidthDefault)
        paramsDouble.Add("Шаг георешеток", dLayerStepDefault)
        paramsDouble.Add("Наклон дренажных призм", dGravelSlopeDefault)
        paramsDouble.Add("Ширина дренажных призм", dDrenageOffsetDefault)
        paramsDouble.Add("Отступ первого слоя георешетки", dBaseElevationDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        paramsLong.Add(Utilities.Side, SideDefault)
        'paramsLong.Add("Отступ первого слоя дренажа", dDrenageElevationDefault)
        paramsLong.Add("Кол-во RE580", dRE580_countDefault)
        paramsLong.Add("Кол-во RE570", dRE570_countDefault)
        paramsLong.Add("Кол-во RE560", dRE560_countDefault)
        paramsLong.Add("Кол-во RE540", dRE540_countDefault)
        paramsLong.Add("Кол-во RE520", dRE520_countDefault)
        'paramsString.Add("Имя участка", dSubAsNameDefault)
        paramsLong.Add("bCount", dBlocksCount)
        paramsDouble.Add("bHeight", dBlockH)
        paramsLong.Add("BlocksAboveGrid", blocksAboveGrid)
        'paramsDouble.Add("RE580_length", dRE580_lengthDefault)
        'paramsDouble.Add("RE570_length", dRE570_lengthDefault)
        'paramsDouble.Add("RE560_length", dRE560_lengthDefault)
        'paramsDouble.Add("RE540_length", dRE540_lengthDefault)
        'paramsDouble.Add("RE520_length", dRE520_lengthDefault)
    End Sub

    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

    End Sub

    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        'Dim oParamsSurface As ParamSurfaceCollection
        'oParamsSurface = corridorState.ParamsSurface

        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget
        'Dim oIntersectionPointWithSurface As IPoint = Nothing

        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

#Region "Присваивание переменным значений параметров"

        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try
        '----------------------------------------
        'flip about Y axis
        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '----------------------------------------
        'geogrid dimensions
        Dim oGridWidth As Double
        Try
            oGridWidth = paramsDouble.Value("Длина георешеток")
        Catch
            oGridWidth = dGridWidthDefault
        End Try
        '----------------------------------------
        Dim oHorizStep As Double
        Try
            oHorizStep = paramsDouble.Value("Горизонтальное смещение")
        Catch
            oHorizStep = dHorizOffset
        End Try
        '----------------------------------------
        Dim oFaceAngle As Double
        Try
            oFaceAngle = paramsDouble.Value("Наклон лицевой грани")
        Catch
            oFaceAngle = dFaceAngleDefault
        End Try
        '----------------------------------------
        Dim gStep As Double
        Try
            gStep = paramsDouble.Value("Шаг георешеток")
        Catch
            gStep = dLayerStepDefault
        End Try
        '----------------------------------------
        Dim oDrenageSlope As Double
        Try
            oDrenageSlope = paramsDouble.Value("Заложение дренажных призм")
        Catch
            oDrenageSlope = dGravelSlopeDefault
        End Try
        '----------------------------------------
        Dim oDrenageOffset As Double
        Try
            oDrenageOffset = paramsDouble.Value("Ширина дренажных призм")
        Catch
            oDrenageOffset = dDrenageOffsetDefault
        End Try
        '----------------------------------------
        'Dim oDrenageElevation As Long
        'Try
        '    oDrenageElevation = paramsLong.Value("Отступ первого слоя дренажа")
        'Catch
        '    oDrenageElevation = dDrenageElevationDefault
        'End Try
        '----------------------------------------
        Dim oBaseElevation As Double
        Try
            oBaseElevation = paramsDouble.Value("Отступ первого слоя георешетки")
        Catch
            oBaseElevation = dBaseElevationDefault
        End Try
        '----------------------------------------
        Dim gOverlap As Double
        Try
            gOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            gOverlap = dGeotextileOverlapDefault
        End Try
        '----------------------------------------
        Dim RE580_count As Long
        Try
            RE580_count = paramsLong.Value("Кол-во RE580")
        Catch
            RE580_count = dRE580_countDefault
        End Try
        '----------------------------------------
        Dim RE570_count As Long
        Try
            RE570_count = paramsLong.Value("Кол-во RE570")
        Catch
            RE570_count = dRE570_countDefault
        End Try
        '----------------------------------------
        Dim RE560_count As Long
        Try
            RE560_count = paramsLong.Value("Кол-во RE560")
        Catch
            RE560_count = dRE560_countDefault
        End Try
        '----------------------------------------
        Dim RE540_count As Long
        Try
            RE540_count = paramsLong.Value("Кол-во RE540")
        Catch
            RE540_count = dRE540_countDefault
        End Try
        '----------------------------------------
        Dim RE520_count As Long
        Try
            RE520_count = paramsLong.Value("Кол-во RE520")
        Catch
            RE520_count = dRE520_countDefault
        End Try
        '----------------------------------------
        'Dim oSubAsName As String
        'Try
        '    oSubAsName = paramsString.Value("Имя участка")
        'Catch
        '    oSubAsName = dSubAsNameDefault
        'End Try
        '----------------------------------------
        Dim bAboveGrid As Integer
        Try
            bAboveGrid = paramsLong.Value("BlocksAboveGrid")
        Catch
            bAboveGrid = blocksAboveGrid
        End Try
        '----------------------------------------
        Dim blockHeight As Double
        Try
            blockHeight = paramsDouble.Value("bHeight")
        Catch
            blockHeight = dBlockH
        End Try
        '----------------------------------------
        Dim blockCount As Long
        Try
            blockCount = paramsLong.Value("bCount")
        Catch
            blockCount = dBlocksCount
        End Try
#End Region
        ' Check user input
        If oGridWidth < 3 Then
            Utilities.RecordError(corridorState, CorridorError.ValueTooSmall, "Длина георешеток", "Geogrid")
            oGridWidth = dGridWidthDefault
        End If

        If gStep <= 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanOrEqualToZero, "Шаг георешеток", "Geogrid")
            gStep = dLayerStepDefault
        End If

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        Dim nLoopCounter As Integer
        nLoopCounter = 0
        Dim nLoopLimit As Integer = 50

        Dim dWallWidthOffset As Double = (oGridWidth + 1.0) * flip 'soil width

        If corridorState.Mode <> CorridorMode.Layout Then 'для сечений коридора
            '--------------------------------------------------------
            'анализируем наличие целей для сбора информации в сечении
            '--------------------------------------------------------
            Dim oBlocks = paramsLong.Value("bCount")
            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("Проектный профиль")
            Catch
                elevationTarget = Nothing
            End Try

            Dim hasWallHeightProfile As Boolean
            hasWallHeightProfile = False
            Dim dWallHeightElevation As Double

            If Not elevationTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallHeightElevation = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallHeightProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
                End Try
            End If
            'On Error GoTo ErrorHandler
            'Определяем глубину отступа для грунта засыпки
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Граница засыпки")
            Catch
                offsetTarget = Nothing
            End Try

            Dim hasWallOffsetTarget As Boolean = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffset As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffset, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    dWallWidthOffset = soilOffset - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Граница засыпки", "RetainWallHorizontal")
                End Try
            End If
            '--------------------------------
            'построение конструкции в сечении
            '--------------------------------
            wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, oGridWidth, gStep, oHorizStep, oBaseElevation, oDrenageOffset, oDrenageSlope, gOverlap, oFaceAngle, flip, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count, blockCount, blockHeight, hasWallOffsetTarget, hasWallHeightProfile, oOrigin)

        Else 'для представления шаблона конструкции

            Dim hasOffsetTarget As Boolean = False
            Dim dWallHeightElevation As Double = oBaseElevation + (RE520_count + RE540_count + RE560_count + RE570_count + RE580_count) * gStep
            Dim hasWallHeightProfile As Boolean = False

            'Dim blocks As Integer = dWallHeightElevation * 1000 / (gStep * 1000)
            wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, oGridWidth, gStep, oHorizStep, oBaseElevation, oDrenageOffset, oDrenageSlope, gOverlap, oFaceAngle, flip, RE520_count, RE540_count, RE560_count, RE570_count, RE580_count, blockCount, blockHeight, hasOffsetTarget, hasWallHeightProfile, oOrigin)

        End If
        ' Обновляем входные параметры (если требуется)
        Dim param As IParam

        param = paramsDouble.Add("Горизонтальное смещение", oHorizStep)
        param = paramsDouble.Add("Наклон лицевой грани", oFaceAngle)
        param = paramsDouble.Add("Длина георешеток", oGridWidth)
        param = paramsDouble.Add("Шаг георешеток", gStep)
        param = paramsDouble.Add("Заложение дренажных призм", oDrenageSlope)
        param = paramsDouble.Add("Ширина дренажных призм", oDrenageOffset)
        param = paramsDouble.Add("Отступ первого слоя георешетки", oBaseElevation)
        param = paramsDouble.Add("Перехлест геотекстиля", gOverlap)
        param = paramsLong.Add(Utilities.Side, side)
        'param = paramsLong.Add("Отступ первого слоя дренажа", oDrenageElevation)
        param = paramsLong.Add("Кол-во RE580", RE580_count)
        param = paramsLong.Add("Кол-во RE570", RE570_count)
        param = paramsLong.Add("Кол-во RE560", RE560_count)
        param = paramsLong.Add("Кол-во RE540", RE540_count)
        param = paramsLong.Add("Кол-во RE520", RE520_count)
        'param = paramsString.Add("Имя участка", oSubAsName)
        param = paramsLong.Add("BlocksAboveGrid", bAboveGrid)
        param = paramsLong.Add("bCount", blockCount)
        param = paramsDouble.Add("bHeight", blockHeight)
    End Sub


    'создание конструкции
    Private Sub wallCreate(ByVal corridorState As CorridorState,
                           ByVal wallHeight As Double,
                           ByVal wallWidth As Double,
                           ByVal gridWidth As Double,
                           ByVal verticalStep As Double,
                           ByVal horizontalStep As Double,
                           ByVal baseLayerStep As Double,
                           ByVal drenageWidth As Double,
                           ByVal drenageSlope As Double,
                           ByVal geotxtOverlap As Double,
                           ByVal faceAngle As Double,
                           ByVal flipValue As Double,
                           ByVal RE520Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE580Count As Long,
                           ByVal blocksCount As Integer,
                           ByVal blockHeight As Double,
                           ByVal hasTargetOffset As Boolean,
                           ByVal hasElevationTarget As Boolean,
                           ByVal startInputPoint As PointInMem
                           )
        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem
        Dim elevatP As Double = 0 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = 0
        insertPoint.Offset = offsetP 'присваиваем значения опорной точке
        insertPoint.Elevation = elevatP

        Dim dX As Double = (horizontalStep + verticalStep * Math.Tan(faceAngle * Math.PI / 180)) * flipValue 'отступ для каждого вышележащего ряда (в метрах) 
        'определим кол-во слоев
        Dim layers As Integer
        layers = RE580Count + RE570Count + RE560Count + RE540Count + RE520Count
        'layers = wallHeight * 1000 \ verticalStep * 1000
        'определим остаток сверху
        'Dim reminder As Double
        'reminder = wallHeight Mod verticalStep
        Dim blocksInLayer As Double = verticalStep / blockHeight 'блоков в одном слое
        Dim blockCountForLayers As Integer = blocksCount - ((baseLayerStep * 1000) \ (blockHeight * 1000)) 'блоков выше первого слоя решетки
        Dim maxLayers As Integer = blockCountForLayers \ CType(blocksInLayer, Integer) 'максимальное ЦЕЛОЕ число слоев исходя из кол-ва облицовочных блоков 
        Dim blocksReminder As Integer = blockCountForLayers Mod CType(blocksInLayer, Integer) 'остаток облицовочных блоков выше ЦЕЛОГО числа слоев
        'If layers > maxLayers Then 'условие не выше облицовки
        '    If blocksInLayer > 1 And blocksReminder = 0 Then 'доп условие для маленького блока
        '        layers = maxLayers - 1
        '        blocksReminder = blocksInLayer
        '    Else 'для большого блока или если у маленького есть хотя бы один блок выше последнего слоя
        '        layers = maxLayers
        '    End If
        'End If
        Dim geotextileName As String = "Геотекстиль"
        Dim soilName As String = "Дренирующий грунт"
        Dim drenageName As String = "Щебень дренажной призмы"
        Dim gridNameRE520 As String = "Георешетка RE520"
        Dim gridNameRE540 As String = "Георешетка RE540"
        Dim gridNameRE560 As String = "Георешетка RE560"
        Dim gridNameRE570 As String = "Георешетка RE570"
        Dim gridNameRE580 As String = "Георешетка RE580"

        Dim isLast As Boolean = False
        Dim isDrenLayers As Boolean = False
        Dim isFirst As Boolean = True
        'Dim linkName As String

        Dim i As Integer = 0

        'песок ниже первой георешетки
        createDrenageLayer(corridorState, drenageName, soilName, wallWidth, baseLayerStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset)
        elevatP += baseLayerStep
        offsetP += baseLayerStep * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        i += 1
        If hasElevationTarget Then
            'ЦИКЛ СОЗДАЮЩИЙ СЛОИ АРМОГРУНТА И ГЕОРЕШЕТКИ (без верхнего слоя)
            layers = (wallHeight - baseLayerStep) * 1000 \ verticalStep * 1000
            Do While layers >= i

                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
                createDrenageLayer(corridorState, drenageName, soilName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset)

                gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
                elevatP += verticalStep
                offsetP += dX
                i += 1
            Loop
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP
            'добавляем еще один слой решетки
            gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
            'проводим анализ оставшегося пространства
            Dim reminder As Double = wallHeight - elevatP 'остаток
            createDrenageLayer(corridorState, drenageName, soilName, wallWidth, reminder, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset)
        Else
            Do While layers > i

                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
                createDrenageLayer(corridorState, drenageName, soilName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset)

                gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
                elevatP += verticalStep
                offsetP += dX
                i += 1
            Loop
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP
            'добавляем еще один слой решетки
            gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count)
        End If

    End Sub
    'создание георешетки
    Private Sub createGeogrid(ByVal corridorState As CorridorState,
                              ByVal linkName As String,
                              ByVal geogridWidth As Double,
                              ByVal flipValue As Double,
                              ByVal pointToInsert As PointInMem)
        '---------------------------------------------------------
        ' создание точек и связи между ними
        '---------------------------------------------------------
        Dim geogridPoints As PointCollection
        geogridPoints = corridorState.Points

        Dim geogridLinks As LinkCollection
        geogridLinks = corridorState.Links

        Dim gridPoint1 As Point
        Dim gridPoint2 As Point
        Dim gridLink As Link

        gridPoint1 = geogridPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, linkName + "1")
        gridPoint2 = geogridPoints.Add(pointToInsert.Offset + geogridWidth * flipValue, pointToInsert.Elevation, linkName + "2")
        gridLink = geogridLinks.Add(gridPoint1, gridPoint2, linkName)

    End Sub
    'добавление соответствующего слоя георешетки
    Private Sub gridlayers(ByVal corridorstate As CorridorState,
                           ByVal i As Integer,
                           ByVal insertPoint As PointInMem,
                           ByVal flipValue As Double,
                           ByVal gridWidth As Double,
                           ByVal gridNameRE580 As String,
                           ByVal gridNameRE570 As String,
                           ByVal gridNameRE560 As String,
                           ByVal gridNameRE540 As String,
                           ByVal gridNameRE520 As String,
                           ByVal RE580Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE520Count As Long
                           )
        Try 'логика присваивания названия слоя
            If i <= RE580Count Then
                'linkName = subAsName + " " + i.ToString + "_RE580"
                createGeogrid(corridorstate, gridNameRE580, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count < i And i <= RE580Count + RE570Count Then
                'linkName = subAsName + " " + i.ToString + "_RE570"
                createGeogrid(corridorstate, gridNameRE570, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count < i And i <= RE580Count + RE570Count + RE560Count Then
                'linkName = subAsName + " " + i.ToString + "_RE560"
                createGeogrid(corridorstate, gridNameRE560, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count Then
                'linkName = subAsName + " " + i.ToString + "_RE540"
                createGeogrid(corridorstate, gridNameRE540, gridWidth, flipValue, insertPoint)
            ElseIf RE580Count + RE570Count + RE560Count + RE540Count < i And i <= RE580Count + RE570Count + RE560Count + RE540Count + RE520Count Then
                'linkName = subAsName + " " + i.ToString + "_RE520"
                createGeogrid(corridorstate, gridNameRE520, gridWidth, flipValue, insertPoint)
            End If
        Catch
            Utilities.RecordWarning(corridorstate, CorridorError.None, "no reinforcement", "ReinfSoilArray")
        End Try
    End Sub
    Private Sub createDrenageLayer(ByVal corridorState As CorridorState,
                                   ByVal drenageName As String,
                                   ByVal soilName As String,
                                   ByVal soilWidth As Double,
                                   ByVal layerHeight As Double,
                                   ByVal faceAngle As Double,
                                   ByVal drenageWidth As Double,
                                   ByVal drenageSlope As Double,
                                   ByVal flipValue As Double,
                                   ByVal pointToInsert As PointInMem,
                                   ByVal geotxtOverlap As Double,
                                   ByVal layerCounter As Integer,
                                   ByVal hasTargetOffset As Boolean
                                   )
        'вычисляем вспомогательные параметры
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim fOffset As Double = layerHeight * Math.Tan(faceSlope) * flipValue 'layer FaceOffset
        Dim dOffset As Double = (layerHeight - 0.05) * drenageSlope * flipValue 'layer DrenageOffset
        Dim gOffset As Double = drenageWidth * flipValue 'gravel offset
        Dim gtxtOffset = geotxtOverlap * flipValue 'перехлест геотекстиля
        '----------------------------
        'drenage layer
        '----------------------------
        'объявляем коллекции элементов
        Dim drenagePoints As PointCollection = corridorState.Points
        Dim drenageLinks As LinkCollection = corridorState.Links
        Dim drenageShapes As ShapeCollection = corridorState.Shapes
        'имена точек щебня
        Dim drPointName1 As String = "_" & CStr(layerCounter) & "_" & "gravel" & 1
        Dim drPointName2 As String = "_" & CStr(layerCounter) & "_" & "gravel" & 2
        Dim drPointName3 As String = "_" & CStr(layerCounter) & "_" & "gravel" & 3
        Dim drPointName4 As String = "_" & CStr(layerCounter) & "_" & "gravel" & 4
        Dim drPointName5 As String = "_" & CStr(layerCounter) & "_" & "gravel" & 5
        'строим слой по точкам
        Dim drPoint1 = drenagePoints.Add(pointToInsert.Offset, pointToInsert.Elevation, drPointName1)
        Dim drPoint2 = drenagePoints.Add(drPoint1.Offset + fOffset, drPoint1.Elevation + layerHeight, drPointName2)
        Dim drPoint3 = drenagePoints.Add(drPoint2.Offset + gOffset, drPoint2.Elevation, drPointName3)
        Dim drPoint4 = drenagePoints.Add(drPoint3.Offset + dOffset, pointToInsert.Elevation + 0.05, drPointName4)
        Dim drPoint5 = drenagePoints.Add(drPoint4.Offset - 0.05 * flipValue, pointToInsert.Elevation, drPointName5)
        'declare description for links
        Dim drLinkName1 As String = "_" & CStr(layerCounter) & "_" & "gravelUp"
        Dim drLinkName2 As String = "_" & CStr(layerCounter) & "_" & "Drenagelayer"
        'create links of gravel layer
        Dim drLink1 = drenageLinks.Add(drPoint1, drPoint2, drLinkName1)
        Dim drLink2 = drenageLinks.Add(drPoint2, drPoint3, drLinkName1)
        Dim drLink3 = drenageLinks.Add(drPoint3, drPoint4, drLinkName2)
        Dim drLink4 = drenageLinks.Add(drPoint4, drPoint5, drLinkName2)
        Dim drLink5 = drenageLinks.Add(drPoint1, drPoint5, drLinkName2)
        'create shape for gravel layer
        Dim drShapeName As String = "_" & drenageName '& CStr(grNumber)
        Dim grSLinks() = {drLink1, drLink2, drLink3, drLink4, drLink5}
        Dim drShape = drenageShapes.Add(grSLinks, drShapeName)
        '----------------------------
        'sand layer
        '----------------------------
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point
        Dim sandPoint5 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link
        Dim sandLink5 As Link

        Dim sandShape As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        Dim sandPointName5 As String = "_" & CStr(layerCounter) & "_" & "sandBase" & 5
        'имена связей при построении сечения
        Dim sandLinkName1 As String = "_" & CStr(layerCounter) & "_" & "sandUpBase"
        Dim sandLinkName2 As String = "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(drPoint3.Offset, drPoint3.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(drPoint4.Offset, drPoint4.Elevation, sandPointName2)
        sandPoint3 = sandPoints.Add(drPoint5.Offset, drPoint5.Elevation, sandPointName3)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint4 = sandPoints.Add(soilWidth, drPoint1.Elevation, sandPointName3)
            sandPoint5 = sandPoints.Add(soilWidth, drPoint2.Elevation, sandPointName4)
        Else
            sandPoint4 = sandPoints.Add(drPoint1.Offset + soilWidth, drPoint1.Elevation, sandPointName3)
            sandPoint5 = sandPoints.Add(drPoint2.Offset + soilWidth, drPoint2.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, sandLinkName1)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, sandLinkName1)
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, sandLinkName2)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint5, sandLinkName2)
        sandLink5 = sandLinks.Add(sandPoint5, sandPoint1, sandLinkName2)
        ' создание заполнения контура песка
        Dim sandSLinks() = {sandLink1, sandLink2, sandLink3, sandLink4, sandLink5}
        sandShape = sandShapes.Add(sandSLinks, "_" & soilName)
        '-------------------------
        'geotextile
        '-------------------------
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation - 0.002, "")
        Dim geotextilePoint2 As Point = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation - 0.002, "")
        Dim geotextilePoint3 As Point = geotxtPoints.Add(sandPoint3.Offset, sandPoint3.Elevation, "")
        Dim geotextilePoint4 As Point = geotxtPoints.Add(drPoint1.Offset, drPoint1.Elevation, "")
        Dim geotextilePoint5 As Point = geotxtPoints.Add(drPoint2.Offset, drPoint2.Elevation, "")
        Dim geotextilePoint6 As Point = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, "")
        Dim geotextilePoint7 As Point = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation + 0.002, "")
        Dim geotextilePoint8 As Point = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation + 0.002, "")
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, "")
        Dim geotextileLink2 As Link = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, "")
        Dim geotextileLink3 As Link = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, "")
        Dim geotextileLink4 As Link = geotxtLinks.Add(geotextilePoint4, geotextilePoint5, "")
        Dim geotextileLink5 As Link = geotxtLinks.Add(geotextilePoint5, geotextilePoint6, "")
        Dim geotextileLink6 As Link = geotxtLinks.Add(geotextilePoint6, geotextilePoint7, "")
        Dim geotextileLink7 As Link = geotxtLinks.Add(geotextilePoint7, geotextilePoint8, "")
    End Sub

End Class

