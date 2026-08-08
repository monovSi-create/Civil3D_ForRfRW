Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports System.Math
Imports Shape = Autodesk.Civil.DatabaseServices.Shape
Imports OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode
Imports System.IO

Public Class ReinfSlope_TMAT
    Inherits SATemplate

    Private Const dFaceAngleDefault = 4
    Private Const dHorizOffset = 0.0
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const dGridWidthDefault = 3.0
    Private Const dLayerStepDefault = 0.45
    Private Const dGravelSlopeDefault = 1.5
    Private Const dDrenageOffsetDefault = 0.3
    Private Const dDrenageElevationDefault = 1
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dBaseElevationDefault = 0.15
    Private Const dRE520_countDefault = 0
    Private Const dRE540_countDefault = 0
    Private Const dRE560_countDefault = 0
    Private Const dRE570_countDefault = 0
    Private Const dRE580_countDefault = 0
    Private Const dSubAsNameDefault = "Участок"
    'Private Const blocksAboveGrid As Integer = 0
    'Private Const dBlocksCount As Integer = 1
    'Private Const dBlockH = 0.5
    Private Const dBlockWidth = 0.214
    Private Const dBlocksInLayout = 5
    Private Const dBlockHeight = 0.15
    Private Const deltaH = 0.000
    Private Const dBlockOffset = 0.010459
    Private Const dBlockLength = 0.4
    Private Const dPipeSlope = 0.05
    Private Const dPipeStep = 5.0
    Private Const dPipeDiametr = 0.16
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

        ParamLong = paramsLong.Add("blocksTop", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль облицовочных блоков"

        ParamLong = paramsLong.Add("BackProf", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Профиль тыльной стороны"
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

        'paramsDouble.Add("Наклон лицевой грани", dFaceAngleDefault)
        'paramsDouble.Add("Горизонтальное смещение", dHorizOffset)
        paramsDouble.Add("Длина георешеток", dGridWidthDefault)
        paramsDouble.Add("Шаг георешеток", dLayerStepDefault)
        paramsDouble.Add("Наклон дренажных призм", dGravelSlopeDefault)
        paramsDouble.Add("Ширина дренажных призм", dDrenageOffsetDefault)
        paramsDouble.Add("Отступ первого слоя георешетки", dBaseElevationDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsLong.Add("Отступ первого слоя дренажа", dDrenageElevationDefault)
        paramsLong.Add("Кол-во RE580", dRE580_countDefault)
        paramsLong.Add("Кол-во RE570", dRE570_countDefault)
        paramsLong.Add("Кол-во RE560", dRE560_countDefault)
        paramsLong.Add("Кол-во RE540", dRE540_countDefault)
        paramsLong.Add("Кол-во RE520", dRE520_countDefault)
        paramsString.Add("Имя участка", dSubAsNameDefault)
        'paramsLong.Add("bCount", dBlocksCount)
        'paramsDouble.Add("bHeight", dBlockH)
        'paramsLong.Add("BlocksAboveGrid", blocksAboveGrid)
        'paramsDouble.Add("RE580_length", dRE580_lengthDefault)
        'paramsDouble.Add("RE570_length", dRE570_lengthDefault)
        'paramsDouble.Add("RE560_length", dRE560_lengthDefault)
        'paramsDouble.Add("RE540_length", dRE540_lengthDefault)
        'paramsDouble.Add("RE520_length", dRE520_lengthDefault)
        paramsLong.Add("BlocksInLayout", dBlocksInLayout)
        paramsDouble.Add("BlocksDeltaH", deltaH)
        paramsDouble.Add("BlockLength", dBlockLength)
        paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
    End Sub
    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

    End Sub
    Protected Overrides Sub DrawImplement(corridorState As CorridorState)
        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager
        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget
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
        'Dim oHorizStep As Double
        'Try
        '    oHorizStep = paramsDouble.Value("Горизонтальное смещение")
        'Catch
        '    oHorizStep = dHorizOffset
        'End Try
        '----------------------------------------
        'Dim oFaceAngle As Double
        '    Try
        '        oFaceAngle = paramsDouble.Value("Наклон лицевой грани")
        '    Catch
        '        oFaceAngle = dFaceAngleDefault
        '    End Try
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
        Dim oDrenageElevation As Long
        Try
            oDrenageElevation = paramsLong.Value("Отступ первого слоя дренажа")
        Catch
            oDrenageElevation = dDrenageElevationDefault
        End Try
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
        Dim oSubAsName As String
        Try
            oSubAsName = paramsString.Value("Имя участка")
        Catch
            oSubAsName = dSubAsNameDefault
        End Try
        '----------------------------------------
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
        '----------------------------------------
        'Dim blockHeight As Double
        'Try
        '    blockHeight = paramsDouble.Value("bHeight")
        'Catch
        '    blockHeight = dBlockH
        'End Try
        '----------------------------------------
        ' Dim blockCount As Long
        ' Try
        '     blockCount = paramsLong.Value("bCount")
        ' Catch
        '     blockCount = dBlocksCount
        ' End Try
        '----------------------------------------
        'Dim blockLayers As Long
        'Try
        '    blockLayers = paramsLong.Value("BlocksInLayout")
        'Catch
        '    blockLayers = dBlocksInLayout
        'End Try

        Dim dH As Double
        Try
            dH = paramsDouble.Value("BlocksDeltaH")
        Catch
            dH = deltaH
        End Try

        Dim dL As Double
        Try
            dL = paramsDouble.Value("BlockLength")
        Catch
            dL = dBlockLength
        End Try
        '----------------------------------------
        Dim oPipeStep As Double
        Try
            oPipeStep = paramsDouble.Value("Шаг дренажных выпусков") / 2
        Catch
            oPipeStep = dPipeStep / 2
        End Try
        '-----------------------
        Dim oPipeSlope As Double
        Try
            oPipeSlope = paramsDouble.Value("Уклон дренажной трубы")
        Catch
            oPipeSlope = dPipeSlope
        End Try
        '-----------------------
        Dim oPipeD As Double
        Try
            oPipeD = paramsDouble.Value("Диаметр дренажной трубы")
        Catch
            oPipeD = dPipeDiametr
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

        Dim dWallWidthOffset As Double = (oGridWidth + 1.0) * flip 'soil width
        Dim blockStep = dBlockHeight + dH

        If corridorState.Mode <> CorridorMode.Layout Then 'для сечений коридора
            '--------------------------------------------------------
            'анализируем наличие целей для сбора информации в сечении
            '--------------------------------------------------------
            'Dim oBlocks = paramsLong.Value("bCount")
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

            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False

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

            'определяем профиль облицовочных блоков (если имеется)
            Dim blocksElevTarget As SlopeElevationTarget
            Try
                blocksElevTarget = oParamsElevationTarget.Value("blocksTop")
            Catch
                blocksElevTarget = Nothing
            End Try

            Dim hasWallBlocksProfile As Boolean
            hasWallBlocksProfile = False
            Dim blocksHeight As Double

            If Not blocksElevTarget Is Nothing Then
                'получим высоту по профилю
                Try
                    blocksHeight = blocksElevTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallBlocksProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "blocksTop", "RetainWallVertical")
                End Try
                'сечение по заданному профилю высоты блоков+проектному
            End If
            'Определяем есть ли профиль ТЫЛЬНОЙ стороны и его значение
            Dim backTarget As SlopeElevationTarget
            Try
                backTarget = oParamsElevationTarget.Value("BackProf")
            Catch
                backTarget = Nothing
            End Try

            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double

            If Not backTarget Is Nothing Then
                'get elevation on elevationTarget
                Try
                    dWallBackElevation = backTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation, side) - (oOrigin.Elevation)
                    hasWallBackProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "BackProf", "RetainWallVertical")
                End Try
            End If

            Dim rows As Integer
            If hasWallBlocksProfile Then 'если есть верхний профиль облицовочных блоков
                rows = CType(blocksHeight / blockStep, Integer)
                createAddStationsForProfile(tm, corridorState, blocksElevTarget)

            Else 'в случае отсутствия профиля для определения высоты облицовки (для первого прохода например)
                'в начале каждого региона(области) добавляем сечения в пикетах шага облицовочного блока TW1
                If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                    'создаем доп.сечения
                    createAddStations(tm, corridorState, blockStep, dL, elevationTarget) 'доп сечения для облицовки
                    createPipeAddStations(tm, corridorState, oPipeStep, oPipeSlope) 'доп сечения для трубы
                    ' Рассчитываем новое количество блоков на основе высоты
                    Dim divisor = blockStep * 1000
                    _blocksCount = dWallHeightElevation * 1000 \ divisor
                    'доп условие: если стена опускается с самого начала
                    Dim firstTop As Double = 0
                    While firstTop <= dL / 2
                        If isStep(tm, corridorState, firstTop) Then
                            _blocksCount -= 1
                        Else
                            Throw New Exception("что-то неладное")
                        End If
                        firstTop += 0.001
                    End While
                End If

                'условие для переопределения высоты облицовки
                If isStep(tm, corridorState, corridorState.CurrentStation) And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation And corridorState.CurrentStation <> corridorState.CurrentRegionStartStation + 0.001 Then
                    'вспомогательные вектора до и после скачка для оценки направления проектного профиля
                    Dim beforeStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation - 0.01)
                    Dim afterStep = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation + 0.01)
                    'сравниваем текущую высоту по блокам и высоту луча(общую высоту стенки)
                    If beforeStep < afterStep Then
                        _blocksCount += 1
                    ElseIf beforeStep > afterStep Then
                        _blocksCount -= 1
                    Else
                        Throw New Exception("что-то неладное")
                    End If
                End If
                rows = _blocksCount
            End If
            'paramsLong.Item("BlocksCount").Value = rows
            Dim lowerLayerH As Double = oBaseElevation
            Dim levelWidth = 0.21 'ширина выравнивающего слоя по облицовке

            '--------------------------------
            'построение конструкции в сечении
            '--------------------------------
            Dim pointToSoil As New PointInMem
            Dim zeroPoint As New PointInMem With {
            .Offset = 0,
            .Elevation = 0
            }
            'создание облицовочных блоков
            createFacingTW(corridorState, dWallHeightElevation, flip, rows, blockStep, dH, levelWidth, zeroPoint, pointToSoil)
            pointToSoil.Offset += flip * (dBlockWidth / 2 - 0.01) 'т.к. точка вставки = середине нижнего блока, смещаем ее на пол блока за исключением фаски
            'создание армогрунта
            wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, dWallBackElevation,
                   oGridWidth, gStep, dHorizOffset,
                   lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope,
                   geotextileOverlap, dFaceAngleDefault, flip, oSubAsName,
                   RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
                   rows, blockStep,
                   hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil)
        Else 'для представления шаблона конструкции
            '----------------------------------
            'строим шаблон конструкции
            '----------------------------------
            Dim levelWidth = 0.21
            Dim levelH = 0.1
            'Dim dWallHeight = blockLayers * (dBlockHeight + dH) + levelH
            Dim hasTarget As Boolean = False
            Dim dWallHeightElevation As Double = oBaseElevation + (RE520_count + RE540_count + RE560_count + RE570_count + RE580_count) * gStep + levelH
            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False
            Dim hasWallBackProfile As Boolean = False
            Dim dWallBackElevation As Double
            Dim blockLayers As Integer = dWallHeightElevation * 1000 \ blockStep * 1000
            Dim lowerLayerH As Double = oBaseElevation
            'точка для вставки армогрунта
            Dim pointToSoil As New PointInMem

            'создание облицовочных блоков
            createFacingTW(corridorState, dWallHeightElevation, flip, blockLayers, blockStep, dH, levelWidth, oOrigin, pointToSoil)
            pointToSoil.Offset += flip * (dBlockWidth / 2 - 0.01) 'т.к. точка вставки = середине нижнего блока, смещаем ее на пол блока за исключением фаски
            wallCreate(corridorState, dWallHeightElevation, dWallWidthOffset, dWallBackElevation,
oGridWidth, gStep, dHorizOffset,
lowerLayerH, oDrenageElevation, oDrenageOffset, oDrenageSlope,
geotextileOverlap, dFaceAngleDefault, flip, oSubAsName,
RE520_count, RE540_count, RE560_count, RE570_count, RE580_count,
blockLayers, blockStep,
hasWallOffsetTarget, hasWallBackProfile, oPipeStep, oPipeSlope, oPipeD, pointToSoil)
        End If
        ' Обновляем входные параметры (если требуется)
        Dim param As IParam

        'param = paramsDouble.Add("Горизонтальное смещение", oHorizStep)
        'param = paramsDouble.Add("Наклон лицевой грани", oFaceAngle)
        param = paramsDouble.Add("Длина георешеток", oGridWidth)
        param = paramsDouble.Add("Шаг георешеток", gStep)
        param = paramsDouble.Add("Заложение дренажных призм", oDrenageSlope)
        param = paramsDouble.Add("Ширина дренажных призм", oDrenageOffset)
        param = paramsDouble.Add("Отступ первого слоя георешетки", oBaseElevation)
        param = paramsDouble.Add("Перехлест геотекстиля", gOverlap)
        param = paramsLong.Add(Utilities.Side, side)
        param = paramsLong.Add("Отступ первого слоя дренажа", oDrenageElevation)
        param = paramsLong.Add("Кол-во RE580", RE580_count)
        param = paramsLong.Add("Кол-во RE570", RE570_count)
        param = paramsLong.Add("Кол-во RE560", RE560_count)
        param = paramsLong.Add("Кол-во RE540", RE540_count)
        param = paramsLong.Add("Кол-во RE520", RE520_count)
        param = paramsString.Add("Имя участка", oSubAsName)
        param = paramsDouble.Add("BlocksDeltaH", dH)
        param = paramsDouble.Add("BlockLength", dL)
        'param = paramsLong.Add("BlocksAboveGrid", bAboveGrid)
        'param = paramsLong.Add("bCount", blockCount)
        'param = paramsDouble.Add("bHeight", blockHeight)
    End Sub
    End Sub
#Region "Создание армогрунта"
    'создание конструкции
    Private Sub wallCreate(ByVal corridorState As CorridorState,
                           ByVal wallHeight As Double,
                           ByVal wallWidth As Double,
                           ByVal wallBackHeight As Double,
                           ByVal gridWidth As Double,
                           ByVal verticalStep As Double,
                           ByVal horizontalStep As Double,
                           ByVal baseLayerStep As Double,
                           ByVal drenageElevLayer As Long,
                           ByVal drenageWidth As Double,
                           ByVal drenageSlope As Double,
                           ByVal geotxtOverlap As Double,
                           ByVal faceAngle As Double,
                           ByVal flipValue As Double,
                           ByVal subAsName As String,
                           ByVal RE520Count As Long,
                           ByVal RE540Count As Long,
                           ByVal RE560Count As Long,
                           ByVal RE570Count As Long,
                           ByVal RE580Count As Long,
                           ByVal blocksCount As Integer,
                           ByVal blockHeight As Double,
                           ByVal hasTargetOffset As Boolean,
                           ByVal hasTargetElevation As Boolean,
                           ByVal pipeStep As Double,
                           ByVal pipeSlope As Double,
                           ByVal pipeDiameter As Double,
                           ByVal startInputPoint As PointInMem
                           )
        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem
        Dim elevatP As Double = startInputPoint.Elevation 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = startInputPoint.Offset
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
        If layers > maxLayers Then 'условие не выше облицовки
            If blocksInLayer > 1 And blocksReminder = 0 Then 'доп условие для маленького блока
                layers = maxLayers - 1
                blocksReminder = blocksInLayer
            Else 'для большого блока или если у маленького есть хотя бы один блок выше последнего слоя
                layers = maxLayers
            End If
        End If
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
        createLowerLayer(corridorState, subAsName, soilName, wallWidth, baseLayerStep, faceAngle, flipValue, insertPoint, i, hasTargetOffset)
        elevatP += baseLayerStep
        offsetP += baseLayerStep * Math.Tan(faceAngle * Math.PI / 180) * flipValue
        i += 1
        'ЦИКЛ СОЗДАЮЩИЙ СЛОИ АРМОГРУНТА И ГЕОРЕШЕТКИ (без верхнего слоя)
        Do While layers >= i
            'testPoint = testPoints.Add(offsetP, elevatP, i.ToString())
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP
            If i <= drenageElevLayer Then 'слои ниже дренажной призмы
                If i = drenageElevLayer Then
                    isLast = True
                    If layers > i Then
                        isDrenLayers = True
                    End If
                End If
                createNonDrenageLayer(corridorState, subAsName, soilName, wallWidth, verticalStep, faceAngle, drenageWidth, flipValue, insertPoint, geotxtOverlap, geotextileName, i, hasTargetOffset, isLast, isDrenLayers)
            Else 'If drenageElevLayer < i And i < layers Then 'слои с дренажной призмой
                createDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                isFirst = False
            End If
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
        If i <= drenageElevLayer Then
            createLastNonDrenageLayer(corridorState, soilName, wallWidth, reminder, wallBackHeight, flipValue, insertPoint, geotxtOverlap, geotextileName, hasTargetOffset, hasTargetElevation)
        Else
            If blocksReminder < 3 And reminder <= verticalStep Then
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, hasTargetElevation, isFirst, reminder)
            ElseIf blocksReminder < 3 And reminder > verticalStep Then
                createDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, isFirst, pipeStep, pipeSlope, pipeDiameter)
                reminder -= verticalStep
                elevatP += verticalStep
                offsetP += dX
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, hasTargetElevation, isFirst, reminder)
            ElseIf blocksReminder >= 3 Then
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, False, isFirst, reminder)
                reminder -= blockHeight
                elevatP += blockHeight
                offsetP += blockHeight * Math.Tan(faceAngle * Math.PI / 180) * flipValue
                insertPoint.Offset = offsetP
                insertPoint.Elevation = elevatP
                i += 1
                'добавляем еще один слой решетки
                gridlayers(corridorState, i, insertPoint, flipValue, gridWidth, gridNameRE580, gridNameRE570, gridNameRE560, gridNameRE540, gridNameRE520, RE580Count, RE570Count, RE560Count, RE540Count, RE520Count) 'добавляем слой георешетки
                createLastDrenageLayer(corridorState, subAsName, drenageName, soilName, geotextileName, wallWidth, verticalStep, wallBackHeight, faceAngle, drenageWidth, drenageSlope, flipValue, insertPoint, geotxtOverlap, i, hasTargetOffset, hasTargetElevation, isFirst, reminder)
            Else
                Throw New Exception("какая-то лажа с верхними слоями армогрунта")
            End If
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
    'создание самого нижнего слоя
    Private Sub createLowerLayer(ByVal corridorState As CorridorState,
                                 ByVal subAsName As String,
                                 ByVal soilName As String,
                                 ByVal soilWidth As Double,
                                 ByVal layerHeight As Double,
                                 ByVal faceAngle As Double,
                                 ByVal flipValue As Double,
                                 ByVal pointToInsert As PointInMem,
                                 ByVal layerCounter As Integer,
                                 ByVal hasTargetOffset As Boolean
                                 )
        '----------------------------
        'sand before first grid
        '----------------------------
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim fSOffset As Double = layerHeight * Tan(faceSlope) * flipValue 'firstStepOffset
        'создание коллекций для точек и связей
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        Dim sandLinkName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        Dim sandLinkName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset + fSOffset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, soilName)
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
    End Sub
    'создание слоя засыпки дренирующим грунтом
    Private Sub createNonDrenageLayer(ByVal corridorState As CorridorState,
                                      ByVal subAsName As String,
                                      ByVal soilName As String,
                                      ByVal soilWidth As Double,
                                      ByVal layerHeight As Double,
                                      ByVal faceAngle As Double,
                                      ByVal drenageWidth As Double,
                                      ByVal flipValue As Double,
                                      ByVal pointToInsert As PointInMem,
                                      ByVal geotxtOverlap As Double,
                                      ByVal geotextileName As String,
                                      ByVal layerCounter As Integer,
                                      ByVal hasTargetOffset As Boolean,
                                      ByVal isLastlayer As Boolean,
                                      ByVal isAnyDrenageLayers As Boolean
                                      )
        '----------------------------
        'sand layer
        '----------------------------
        'вычисляем вспомогательные параметры
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim SOffset As Double = layerHeight * Tan(faceSlope) * flipValue 'layerStepOffset
        'создание коллекций для точек,связей и форм
        Dim sandPoints As PointCollection = corridorState.Points
        Dim sandLinks As LinkCollection = corridorState.Links
        Dim sandShapes As ShapeCollection = corridorState.Shapes

        Dim sandPoint1 As Point
        Dim sandPoint2 As Point
        Dim sandPoint3 As Point
        Dim sandPoint4 As Point

        Dim sandLink1 As Link
        Dim sandLink2 As Link
        Dim sandLink3 As Link
        Dim sandLink4 As Link

        Dim sandShape1 As Shape
        'имена точек при построении сечения
        Dim sandPointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 1
        Dim sandPointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 2
        Dim sandPointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 3
        Dim sandPointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "sandBase" & 4
        'имена связей при построении сечения
        'Dim sandLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandUpBase"
        'Dim sandLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "sandDownBase"
        'создание точек контура песка
        sandPoint1 = sandPoints.Add(pointToInsert.Offset, pointToInsert.Elevation, sandPointName1)
        sandPoint2 = sandPoints.Add(sandPoint1.Offset + SOffset, sandPoint1.Elevation + layerHeight, sandPointName2)
        'проверка наличия цели для отступа(ширины стены)
        If hasTargetOffset Then
            sandPoint3 = sandPoints.Add(soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(soilWidth, sandPoint1.Elevation, sandPointName4)
        Else
            sandPoint3 = sandPoints.Add(sandPoint2.Offset + soilWidth, sandPoint2.Elevation, sandPointName3)
            sandPoint4 = sandPoints.Add(sandPoint1.Offset + soilWidth, sandPoint1.Elevation, sandPointName4)
        End If
        'создание линий контура песка
        sandLink1 = sandLinks.Add(sandPoint1, sandPoint2, soilName)
        sandLink2 = sandLinks.Add(sandPoint2, sandPoint3, "")
        sandLink3 = sandLinks.Add(sandPoint3, sandPoint4, soilName)
        sandLink4 = sandLinks.Add(sandPoint4, sandPoint1, "")
        ' создание заполнения контура песка
        sandShape1 = sandShapes.Add(sandLink1, sandLink2, sandLink3, sandLink4, soilName)
        '-------------------------
        'geotextile
        '-------------------------
        'вычисляем вспомогательные параметры
        Dim gtxtOffset = geotxtOverlap * flipValue
        'создание коллекций для точек и связей
        Dim geotxtPoints As PointCollection = corridorState.Points
        Dim geotxtLinks As LinkCollection = corridorState.Links
        'объявим точки для геотекстиля
        Dim geotextilePoint1 As Point
        Dim geotextilePoint2 As Point
        Dim geotextilePoint3 As Point
        Dim geotextilePoint4 As Point
        'объявим связи для геотекстиля
        Dim geotextileLink1 As Link
        Dim geotextileLink2 As Link
        Dim geotextileLink3 As Link

        Dim geotextilePointName1 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 1
        Dim geotextilePointName2 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 2
        Dim geotextilePointName3 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 3
        Dim geotextilePointName4 As String = "" 'subAsName & "_" & CStr(layerCounter) & "_" & "geotextile" & 4

        geotextilePoint1 = geotxtPoints.Add(sandPoint1.Offset + gtxtOffset, sandPoint1.Elevation, geotextilePointName1)
        geotextilePoint2 = geotxtPoints.Add(sandPoint1.Offset, sandPoint1.Elevation, geotextilePointName2)
        geotextilePoint3 = geotxtPoints.Add(sandPoint2.Offset, sandPoint2.Elevation, geotextilePointName3)
        'проверка наличия сверху слоя с дренажом
        If isLastlayer And isAnyDrenageLayers Then
            geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset + drenageWidth * flipValue, sandPoint2.Elevation, geotextilePointName4)
        Else
            geotextilePoint4 = geotxtPoints.Add(sandPoint2.Offset + gtxtOffset, sandPoint2.Elevation, geotextilePointName4)
        End If
        'Dim geotextileLinkName1 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileUp"
        'Dim geotextileLinkName2 As String = subAsName & "_" & CStr(layerCounter) & "_" & "geotextileDown"

        geotextileLink1 = geotxtLinks.Add(geotextilePoint1, geotextilePoint2, geotextileName)
        geotextileLink2 = geotxtLinks.Add(geotextilePoint2, geotextilePoint3, geotextileName)
        geotextileLink3 = geotxtLinks.Add(geotextilePoint3, geotextilePoint4, geotextileName)
    End Sub
#End Region
End Class
