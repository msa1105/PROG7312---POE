import { useEffect, useRef } from 'react';

type SparklineProps = {
  data: number[];
  isAnomaly: boolean;
  isOffline: boolean;
};

export const Sparkline = ({ data, isAnomaly, isOffline }: SparklineProps) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.clearRect(0, 0, canvas.width, canvas.height);

    if (data.length === 0) return;

    const max = Math.max(...data, 1);
    const min = Math.min(...data, 0);
    const range = max - min || 1;

    const stepX = canvas.width / (Math.max(data.length - 1, 1));
    
    ctx.beginPath();
    ctx.strokeStyle = isOffline ? '#f59e0b' : isAnomaly ? '#f43f5e' : '#a1a1aa';
    ctx.lineWidth = 1.5;
    
    if (isOffline) {
      ctx.setLineDash([4, 4]);
    } else {
      ctx.setLineDash([]);
    }

    data.forEach((val, i) => {
      const x = i * stepX;
      // Invert Y axis
      const y = canvas.height - ((val - min) / range) * canvas.height;
      
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });

    ctx.stroke();
  }, [data, isAnomaly, isOffline]);

  return (
    <canvas 
      ref={canvasRef} 
      width={300} 
      height={40} 
      className="w-full h-10 mt-2 opacity-80"
    />
  );
};
